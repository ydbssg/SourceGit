using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia.Collections;

namespace SourceGit.Models
{
    public class RepositoryUIStates
    {
        public HistoryShowFlags HistoryShowFlags
        {
            get;
            set;
        } = HistoryShowFlags.None;

        public bool IsAuthorColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public bool IsSHAColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public bool IsAuthorTimeColumnVisibleInHistory
        {
            get;
            set;
        } = false;

        public bool IsCommitTimeColumnVisibleInHistory
        {
            get;
            set;
        } = true;

        public double AuthorColumnWidth
        {
            get;
            set;
        } = 120;

        public bool EnableTopoOrderInHistory
        {
            get;
            set;
        } = false;

        public CommitGraphHighlighting GraphHighlighting
        {
            get;
            set;
        } = CommitGraphHighlighting.All;

        public BranchSortMode LocalBranchSortMode
        {
            get;
            set;
        } = BranchSortMode.Name;

        public BranchSortMode RemoteBranchSortMode
        {
            get;
            set;
        } = BranchSortMode.Name;

        public bool ShowTagsAsTree
        {
            get;
            set;
        } = false;

        public TagSortMode TagSortMode
        {
            get;
            set;
        } = TagSortMode.CreatorDate;

        public bool ShowSubmodulesAsTree
        {
            get;
            set;
        } = false;

        public bool IncludeUntrackedInLocalChanges
        {
            get;
            set;
        } = true;

        public bool EnableForceOnFetch
        {
            get;
            set;
        } = false;

        public bool FetchAllRemotes
        {
            get;
            set;
        } = false;

        public bool FetchWithoutTags
        {
            get;
            set;
        } = false;

        public bool PullOnlySelectedRemoteBranch
        {
            get;
            set;
        }

        public bool PreferRebaseInsteadOfMerge
        {
            get;
            set;
        } = true;

        public bool CheckSubmodulesOnPush
        {
            get;
            set;
        } = true;

        public bool PushAllTags
        {
            get;
            set;
        } = false;

        public bool CreateAnnotatedTag
        {
            get;
            set;
        } = true;

        public bool PushToRemoteWhenCreateTag
        {
            get;
            set;
        } = true;

        public bool PushToRemoteWhenDeleteTag
        {
            get;
            set;
        } = false;

        public bool CheckoutBranchOnCreateBranch
        {
            get;
            set;
        } = true;

        public bool EnableSignOffForCommit
        {
            get;
            set;
        } = false;

        public bool NoVerifyOnCommit
        {
            get;
            set;
        } = false;

        public bool IncludeUntrackedWhenStash
        {
            get;
            set;
        } = true;

        public bool OnlyStagedWhenStash
        {
            get;
            set;
        } = false;

        public int ChangesAfterStashing
        {
            get;
            set;
        } = 0;

        public bool IsLocalBranchesExpandedInSideBar
        {
            get;
            set;
        } = true;

        public bool IsRemotesExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsTagsExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsSubmodulesExpandedInSideBar
        {
            get;
            set;
        } = false;

        public bool IsWorktreeExpandedInSideBar
        {
            get;
            set;
        } = false;

        public List<string> ExpandedBranchNodesInSideBar
        {
            get;
            set;
        } = [];

        public string LastCommitMessage
        {
            get;
            set;
        } = string.Empty;

        public bool FocusOnCurrentBranch
        {
            get;
            set;
        } = false;

        public AvaloniaList<HistoryFilter> HistoryFilters
        {
            get;
            set;
        } = [];

        public bool IsHistoryFiltersCollapsed
        {
            get;
            set;
        } = false;

        public List<string> RecentCommitMessages
        {
            get;
            set;
        } = [];

        public static RepositoryUIStates Load(string gitDir)
        {
            var fileInfo = new FileInfo(Path.Combine(gitDir, "sourcegit.uistates"));
            var fullpath = fileInfo.FullName;

            RepositoryUIStates states;
            if (!File.Exists(fullpath))
            {
                states = new RepositoryUIStates();
            }
            else
            {
                try
                {
                    using var stream = File.OpenRead(fullpath);
                    states = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.RepositoryUIStates);
                }
                catch
                {
                    states = new RepositoryUIStates();
                }
            }

            states._file = fullpath;
            return states;
        }

        public void Save()
        {
            try
            {
                var content = JsonSerializer.Serialize(this, JsonCodeGen.Default.RepositoryUIStates);
                var tmpfile = $"{_file}.tmp";
                File.WriteAllText(tmpfile, content);
                File.Move(tmpfile, _file, true);
            }
            catch
            {
                // Ignore save errors
            }
        }

        public FilterMode GetHistoryFilterMode(string pattern = null)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                foreach (var filter in HistoryFilters)
                {
                    if (filter.Mode is FilterMode.Included or FilterMode.Excluded)
                        return filter.Mode;
                }

                return FilterMode.None;
            }

            foreach (var filter in HistoryFilters)
            {
                if (filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    return filter.Mode;
            }

            return FilterMode.None;
        }

        public Dictionary<string, FilterMode> GetHistoryFiltersMap()
        {
            var map = new Dictionary<string, FilterMode>();
            foreach (var filter in HistoryFilters)
                map.Add(filter.Pattern, filter.Mode);
            return map;
        }

        public bool UpdateHistoryFilters(string pattern, FilterType type, FilterMode mode)
        {
            if (mode == FilterMode.VisibleInHiddenFolder)
            {
                if (type != FilterType.RemoteBranch || !IsRemoteBranchHiddenByFolder(pattern))
                    return false;
            }
            else if (mode != FilterMode.None)
            {
                // Visibility exceptions use the parent folder's exclusion mode.
                var clear = false;
                foreach (var filter in HistoryFilters)
                {
                    if (filter.Mode != mode && filter.Mode != FilterMode.VisibleInHiddenFolder)
                    {
                        clear = true;
                        break;
                    }
                }

                if (clear)
                {
                    HistoryFilters.Clear();
                    HistoryFilters.Add(new HistoryFilter(pattern, type, mode));
                    return true;
                }
            }
            else
            {
                for (int i = 0; i < HistoryFilters.Count; i++)
                {
                    var filter = HistoryFilters[i];
                    if (filter.Type == type && filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    {
                        HistoryFilters.RemoveAt(i);
                        return true;
                    }
                }

                return false;
            }

            foreach (var filter in HistoryFilters)
            {
                if (filter.Type != type || !filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    continue;

                if (filter.Mode == mode)
                    return false;

                filter.Mode = mode;
                return true;
            }

            HistoryFilters.Add(new HistoryFilter(pattern, type, mode));
            return true;
        }

        public bool IsRemoteBranchHiddenByFolder(string branchRef)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.RemoteBranchFolder &&
                    filter.Mode == FilterMode.Excluded &&
                    branchRef.StartsWith($"{filter.Pattern}/", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public void RemoveOrphanedVisibilityExceptions()
        {
            for (var i = HistoryFilters.Count - 1; i >= 0; i--)
            {
                var filter = HistoryFilters[i];
                if (filter.Mode == FilterMode.VisibleInHiddenFolder && !IsRemoteBranchHiddenByFolder(filter.Pattern))
                    HistoryFilters.RemoveAt(i);
            }
        }

        public bool IsCoveredByFolderFilter(string path, FilterType type)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == type && path.StartsWith($"{filter.Pattern}/", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public void RemoveHistoryFilter(string pattern, FilterType type)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == type && filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                {
                    HistoryFilters.Remove(filter);
                    break;
                }
            }
        }

        public void RenameBranchFilter(string oldName, string newName)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.LocalBranch &&
                    filter.Pattern.Equals(oldName, StringComparison.Ordinal))
                {
                    filter.Pattern = newName;
                    break;
                }
            }
        }

        public void RemoveBranchFiltersByPrefix(string pattern)
        {
            var dirty = new List<HistoryFilter>();
            var prefix = $"{pattern}/";

            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.Tag)
                    continue;

                if (filter.Pattern.StartsWith(prefix, StringComparison.Ordinal))
                    dirty.Add(filter);
            }

            foreach (var filter in dirty)
                HistoryFilters.Remove(filter);
        }

        public string BuildHistoryParams_NormalMode(string gitDir, out Predicate<Decorator> hideDecoration)
        {
            var builder = new StringBuilder();
            hideDecoration = null;

            if (EnableTopoOrderInHistory)
                builder.Append("--topo-order ");
            else
                builder.Append("--date-order ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.Reflog))
                builder.Append("--reflog ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.FirstParentOnly))
                builder.Append("--first-parent ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.SimplifyByDecoration))
                builder.Append("--simplify-by-decoration ");

            var mode = GetHistoryFilterMode();
            if (mode == FilterMode.None)
                builder.Append("--branches --remotes --tags HEAD");
            else if (mode == FilterMode.Included)
                BuildHistoryParamsForIncluded(builder);
            else
                BuildHistoryParamsForExcluded(builder, gitDir, out hideDecoration);

            return builder.ToString();
        }

        public string BuildHistoryParams_FocusOnCurrentBranchMode(string upstream)
        {
            var builder = new StringBuilder();

            if (EnableTopoOrderInHistory)
                builder.Append("--topo-order ");
            else
                builder.Append("--date-order ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.FirstParentOnly))
                builder.Append("--first-parent ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.SimplifyByDecoration))
                builder.Append("--simplify-by-decoration ");

            builder.Append("HEAD ");

            if (!string.IsNullOrEmpty(upstream))
                builder.Append(upstream);

            return builder.ToString();
        }

        private void BuildHistoryParamsForIncluded(StringBuilder builder)
        {
            foreach (var filter in HistoryFilters)
            {
                if (filter.Type == FilterType.LocalBranch)
                    builder.Append(filter.Pattern).Append(' ');
                else if (filter.Type == FilterType.LocalBranchFolder)
                    builder.Append($"--branches={filter.Pattern.AsSpan(11)}/* ");
                else if (filter.Type == FilterType.RemoteBranch)
                    builder.Append(filter.Pattern).Append(' ');
                else if (filter.Type == FilterType.RemoteBranchFolder)
                    builder.Append($"--remotes={filter.Pattern.AsSpan(13)}/* ");
                else if (filter.Type == FilterType.Tag)
                    builder.Append($"refs/tags/{filter.Pattern} ");
            }
        }

        private void BuildHistoryParamsForExcluded(StringBuilder builder, string gitDir, out Predicate<Decorator> hideDecoration)
        {
            var excludedBranches = new List<string>();
            var excludedRemotes = new List<string>();
            var excludedTags = new List<string>();
            var visibleRemoteRefs = new HashSet<string>(StringComparer.Ordinal);
            var partiallyHiddenFolders = new List<string>();

            foreach (var filter in HistoryFilters)
            {
                if (filter is { Type: FilterType.RemoteBranch, Mode: FilterMode.VisibleInHiddenFolder } &&
                    IsRemoteBranchHiddenByFolder(filter.Pattern))
                    visibleRemoteRefs.Add(filter.Pattern);
            }

            foreach (var filter in HistoryFilters)
            {
                if (filter.Mode != FilterMode.Excluded)
                    continue;

                if (filter.Type == FilterType.LocalBranch)
                    excludedBranches.Add($"--exclude=\"{filter.Pattern.AsSpan(11)}\" --decorate-refs-exclude=\"{filter.Pattern}\" ");
                else if (filter.Type == FilterType.LocalBranchFolder)
                    excludedBranches.Add($"--exclude=\"{filter.Pattern.AsSpan(11)}/*\" --decorate-refs-exclude=\"{filter.Pattern}/*\" ");
                else if (filter.Type == FilterType.RemoteBranch)
                    excludedRemotes.Add($"--exclude=\"{filter.Pattern.AsSpan(13)}\" --decorate-refs-exclude=\"{filter.Pattern}\" ");
                else if (filter.Type == FilterType.RemoteBranchFolder)
                {
                    var prefix = $"{filter.Pattern}/";
                    excludedRemotes.Add($"--exclude=\"{prefix.AsSpan(13)}*\" ");
                    if (visibleRemoteRefs.Any(refName => refName.StartsWith(prefix, StringComparison.Ordinal)))
                        partiallyHiddenFolders.Add(prefix[13..]);
                    else
                        excludedRemotes.Add($"--decorate-refs-exclude=\"{prefix}*\" ");
                }
                else if (filter.Type == FilterType.Tag)
                    excludedTags.Add($"--exclude=\"{filter.Pattern}\" --decorate-refs-exclude=\"refs/tags/{filter.Pattern}\" ");
            }

            foreach (var b in excludedBranches)
                builder.Append(b);

            builder.Append("--branches ");

            var isInProgress = File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")) ||
                Directory.Exists(Path.Combine(gitDir, "rebase-merge")) ||
                Directory.Exists(Path.Combine(gitDir, "rebase-apply")) ||
                File.Exists(Path.Combine(gitDir, "REVERT_HEAD")) ||
                File.Exists(Path.Combine(gitDir, "MERGE_HEAD"));
            if (isInProgress)
                builder.Append("HEAD ");

            foreach (var r in excludedRemotes)
                builder.Append(r);

            builder.Append("--exclude=origin/HEAD --remotes ");

            foreach (var branchRef in visibleRemoteRefs)
            {
                var pattern = $"refs/[r]emotes/{branchRef.AsSpan(13)}";
                builder.Append("--glob=").Append(pattern.Quoted()).Append(' ');
            }

            foreach (var t in excludedTags)
                builder.Append(t);

            builder.Append("--tags ");

            hideDecoration = partiallyHiddenFolders.Count == 0
                ? null
                : decoration => decoration.Type == DecoratorType.RemoteBranchHead &&
                    !visibleRemoteRefs.Contains($"refs/remotes/{decoration.Name}") &&
                    partiallyHiddenFolders.Exists(prefix => decoration.Name.StartsWith(prefix, StringComparison.Ordinal));
        }

        public void AddRecentCommitMessage(string message)
        {
            message = message.Trim().ReplaceLineEndings("\n");
            var existIdx = RecentCommitMessages.IndexOf(message);
            if (existIdx == 0)
                return;

            if (existIdx > 0)
            {
                RecentCommitMessages.RemoveAt(existIdx);
                RecentCommitMessages.Insert(0, message);
                return;
            }

            if (RecentCommitMessages.Count > 9)
                RecentCommitMessages.RemoveRange(9, RecentCommitMessages.Count - 9);

            RecentCommitMessages.Insert(0, message);
        }

        private string _file = string.Empty;
    }
}
