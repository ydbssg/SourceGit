using System;
using System.Collections.Generic;
using System.Text;

namespace SourceGit.Models
{
    internal readonly record struct DiffFile(string Revision, string Path);

    public class DiffOption
    {
        public bool IsLocalChange => _revisions.Count == 0;
        public bool IsUnstaged => _isUnstaged;
        public List<string> Revisions => _revisions;
        public string Path => _path;
        public string OrgPath => _orgPath;
        internal DiffFile? OldFile { get; }
        internal DiffFile? NewFile { get; }

        /// <summary>
        ///     Only used for working copy changes
        /// </summary>
        /// <param name="change"></param>
        /// <param name="isUnstaged"></param>
        public DiffOption(Change change, bool isUnstaged)
        {
            _isUnstaged = isUnstaged;
            _path = change.Path;
            _orgPath = change.OriginalPath;

            var state = isUnstaged ? change.WorkTree : change.Index;
            var oldPath = isUnstaged && state != ChangeState.Renamed ? _path : OriginalOrCurrentPath;
            var oldRevision = isUnstaged ? string.Empty : change.DataForAmend?.ParentSHA ?? "HEAD";
            (OldFile, NewFile) = GetFiles(oldRevision, oldPath, isUnstaged ? null : string.Empty, _path, state);

            if (isUnstaged)
            {
                switch (change.WorkTree)
                {
                    case ChangeState.Added:
                    case ChangeState.Untracked:
                        _extra = "--no-index";
                        _orgPath = "/dev/null";
                        break;
                }
            }
            else
            {
                if (change.DataForAmend != null)
                    _extra = $"--cached {change.DataForAmend.ParentSHA}";
                else
                    _extra = "--cached";
            }
        }

        /// <summary>
        ///     Only used for commit changes.
        /// </summary>
        /// <param name="commit"></param>
        /// <param name="change"></param>
        public DiffOption(Commit commit, Change change)
        {
            _revisions.Add(commit.FirstParentToCompare);
            _revisions.Add(commit.SHA);
            _path = change.Path;
            _orgPath = change.OriginalPath;
            (OldFile, NewFile) = GetFiles(_revisions[0], OriginalOrCurrentPath, _revisions[1], _path, change.Index);
        }

        /// <summary>
        ///     Used to diff in `FileHistory`
        /// </summary>
        /// <param name="ver"></param>
        public DiffOption(FileVersion ver)
        {
            if (string.IsNullOrEmpty(ver.OriginalPath))
            {
                _revisions.Add(ver.HasParent ? $"{ver.SHA}^" : EmptyTreeHash.Guess(ver.SHA));
                _revisions.Add(ver.SHA);
                _path = ver.Path;
            }
            else
            {
                _revisions.Add($"{ver.SHA}^:{ver.OriginalPath.Quoted()}");
                _revisions.Add($"{ver.SHA}:{ver.Path.Quoted()}");
                _path = ver.Path;
                _orgPath = ver.Change.OriginalPath;
                _ignorePaths = true;
            }

            (OldFile, NewFile) = GetFiles(ver.HasParent ? $"{ver.SHA}^" : EmptyTreeHash.Guess(ver.SHA),
                OriginalOrCurrentPath, ver.SHA, _path, ver.Change.Index);
        }

        /// <summary>
        ///     Used to diff two revisions in `FileHistory`
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public DiffOption(FileVersion start, FileVersion end)
        {
            OldFile = start.Change.Index == ChangeState.Deleted ? null : new DiffFile(start.SHA, start.Path);
            NewFile = end.Change.Index == ChangeState.Deleted ? null : new DiffFile(end.SHA, end.Path);

            if (start.Change.Index == ChangeState.Deleted)
            {
                _revisions.Add(EmptyTreeHash.Guess(end.SHA));
                _revisions.Add(end.SHA);
                _path = end.Path;
            }
            else if (end.Change.Index == ChangeState.Deleted)
            {
                _revisions.Add(start.SHA);
                _revisions.Add(EmptyTreeHash.Guess(start.SHA));
                _path = start.Path;
            }
            else if (!end.Path.Equals(start.Path, StringComparison.Ordinal))
            {
                _revisions.Add($"{start.SHA}:{start.Path.Quoted()}");
                _revisions.Add($"{end.SHA}:{end.Path.Quoted()}");
                _path = end.Path;
                _orgPath = start.Path;
                _ignorePaths = true;
            }
            else
            {
                _revisions.Add(start.SHA);
                _revisions.Add(end.SHA);
                _path = start.Path;
            }
        }

        /// <summary>
        ///     Used to show differences between two revisions.
        /// </summary>
        /// <param name="baseRevision"></param>
        /// <param name="targetRevision"></param>
        /// <param name="change"></param>
        public DiffOption(string baseRevision, string targetRevision, Change change)
        {
            _revisions.Add(string.IsNullOrEmpty(baseRevision) ? "-R" : baseRevision);
            _revisions.Add(targetRevision);
            _path = change.Path;
            _orgPath = change.OriginalPath;
            (OldFile, NewFile) = GetFiles(string.IsNullOrEmpty(baseRevision) ? null : baseRevision, OriginalOrCurrentPath,
                string.IsNullOrEmpty(targetRevision) ? null : targetRevision, _path, change.Index);
        }

        /// <summary>
        ///     Converts to diff command arguments.
        /// </summary>
        public override string ToString()
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(_extra))
                builder.Append($"{_extra} ");
            foreach (var r in _revisions)
                builder.Append($"{r} ");

            if (_ignorePaths)
                return builder.ToString();

            builder.Append("-- ");
            if (!string.IsNullOrEmpty(_orgPath))
                builder.Append($"{_orgPath.Quoted()} ");
            builder.Append(_path.Quoted());

            return builder.ToString();
        }

        private string OriginalOrCurrentPath => string.IsNullOrEmpty(_orgPath) ? _path : _orgPath;

        private static (DiffFile? Old, DiffFile? New) GetFiles(string oldRevision, string oldPath,
            string newRevision, string newPath, ChangeState state)
        {
            var before = state is ChangeState.Added or ChangeState.Untracked || oldPath == "/dev/null" ||
                (!string.IsNullOrEmpty(oldRevision) && oldRevision == EmptyTreeHash.Guess(oldRevision))
                ? (DiffFile?)null
                : new DiffFile(oldRevision, oldPath);
            var after = state == ChangeState.Deleted || newPath == "/dev/null"
                ? (DiffFile?)null
                : new DiffFile(newRevision, newPath);
            return (before, after);
        }

        private readonly bool _isUnstaged = false;
        private readonly string _path;
        private readonly string _orgPath = string.Empty;
        private readonly string _extra = string.Empty;
        private readonly List<string> _revisions = [];
        private readonly bool _ignorePaths = false;
    }
}
