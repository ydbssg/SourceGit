using System;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    internal static class GitOutputText
    {
        public static string TranslateLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !UseChinese)
                return line;

            // Git aligns ref updates and diff statistics with leading spaces.
            var message = line.TrimStart(' ', '\t');
            var prefix = line[..(line.Length - message.Length)];
            var isRemote = message.StartsWith("remote: ", StringComparison.Ordinal);
            if (isRemote)
                message = message[8..];
            message = message.TrimEnd(' ', '\t');

            foreach (var (pattern, key) in Patterns.Rules)
            {
                var match = pattern.Match(message);
                if (!match.Success)
                    continue;

                var args = new object[match.Groups.Count - 1];
                for (var i = 0; i < args.Length; i++)
                    args[i] = match.Groups[i + 1].Value;
                var translation = App.Text("GitOutput." + key, args);
                return isRemote ? prefix + App.Text("GitOutput.Remote") + translation : prefix + translation;
            }

            return line;
        }

        public static string TranslateMessage(string message)
        {
            if (string.IsNullOrEmpty(message) || !UseChinese)
                return message;

            var lines = message.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                lines[i] = line.EndsWith('\r') ? TranslateLine(line[..^1]) + "\r" : TranslateLine(line);
            }
            return string.Join('\n', lines);
        }

        private static bool UseChinese => App.LocaleKey is "zh_CN" or "zh_TW";

        // These patterns apply to display text only. Keep unrecognized output for diagnostics.
        private static class Patterns
        {
            public static readonly (Regex Pattern, string Key)[] Rules =
            [
                (new(@"^Auto-merging (.+)$"), "AutoMerging"),
                (new(@"^CONFLICT \((?:content|add/add|modify/delete|rename/rename|rename/delete|rename/add|file/directory|directory/file|submodule)\): Merge conflict in (.+)$"), "ConflictContent"),
                (new(@"^warning: Cannot merge binary files: (.+)$"), "ConflictBinary"),
                (new(@"^Automatic merge failed; fix conflicts and then commit the result\.$"), "ConflictMerge"),
                (new(@"^Changes from ([0-9a-f]+) to ([0-9a-f]+):$"), "ChangesFrom"),
                (new(@"^(\d+) files? changed$"), "FilesChanged"),
                (new(@"^(\d+) files? changed, (\d+) insertions?\(\+\)$"), "FilesChangedAdded"),
                (new(@"^(\d+) files? changed, (\d+) deletions?\(-\)$"), "FilesChangedDeleted"),
                (new(@"^(\d+) files? changed, (\d+) insertions?\(\+\), (\d+) deletions?\(-\)$"), "FilesChangedBoth"),
                (new(@"^From (.+)$"), "From"),
                (new(@"^To (.+)$"), "To"),
                (new(@"^= +\[up to date\] +(.+)$"), "UpToDateRef"),
                (new(@"^\* +\[new branch\] +(.+)$"), "NewBranch"),
                (new(@"^\* +\[new tag\] +(.+)$"), "NewTag"),
                (new(@"^\* +branch +(.+ -> .+)$"), "Branch"),
                (new(@"^\* +tag +(.+ -> .+)$"), "Tag"),
                (new(@"^- +\[deleted\] +(.+)$"), "DeletedRef"),
                (new(@"^\+ +(.+?) +\(forced update\)$"), "ForcedUpdate"),
                (new(@"^! +\[rejected\] +(.+?) +\(non-fast-forward\)$"), "Rejected"),
                (new(@"^! +\[rejected\] +(.+?) +\(fetch first\)$"), "RejectedFetchFirst"),
                (new(@"^! +\[rejected\] +(.+?) +\(stale info\)$"), "RejectedStaleInfo"),
                (new(@"^! +\[remote rejected\] +(.+)$"), "RemoteRejected"),
                (new(@"^Already up to date\.$"), "UpToDate"),
                (new(@"^Everything up-to-date$"), "EverythingUpToDate"),
                (new(@"^Fetching (.+)$"), "Fetching"),
                (new(@"^Updating ([0-9a-f]+\.\.[0-9a-f]+)$"), "Updating"),
                (new(@"^Fast-forward$"), "FastForward"),
                (new(@"^Merge made by the '([^']+)' strategy\.$"), "MergeMade"),
                (new(@"^Rebasing \((\d+/\d+)\)$"), "Rebasing"),
                (new(@"^Successfully rebased and updated (.+)\.$"), "RebaseSuccess"),
                (new(@"^error: cannot (?:pull with rebase|rebase): You have unstaged changes\.$"), "RebaseUnstaged"),
                (new(@"^error: (?:cannot (?:pull with rebase|rebase): )?Your index contains uncommitted changes\.$"), "RebaseUncommitted"),
                (new(@"^error: [Pp]lease commit or stash them\.$"), "CommitOrStash"),
                (new(@"^error: could not apply ([0-9a-f]+)\.\.\. (.+)$"), "ApplyErrorWithSubject"),
                (new(@"^error: could not apply ([0-9a-f]+)(?:\.\.\.)?$"), "ApplyError"),
                (new(@"^Could not apply ([0-9a-f]+)\.\.\. (.+)$"), "ApplyStopped"),
                (new(@"^Could not apply ([0-9a-f]+)(?:\.\.\.)?$"), "ApplyStoppedWithoutSubject"),
                (new(@"^Applying: (.+)$"), "ApplyPatch"),
                (new(@"^Patch failed at (.+)$"), "PatchFailed"),
                (new(@"^Encountered (\d+) files? that should have been (?:a )?pointers?, but (?:wasn't|weren't):$"), "LFSPointer"),
                (new(@"^Updated Git hooks\.$"), "LFSUpdated"),
                (new(@"^Git LFS initialized\.$"), "LFSInitialized"),
                (new(@"^Error downloading object: (.+)$"), "LFSErrorDownloading"),
                (new(@"^Downloading LFS objects: (.+)$"), "LFSDownloading"),
                (new(@"^Uploading LFS objects: (.+)$"), "LFSUploading"),
                (new(@"^Filtering content: (.+)$"), "LFSFiltering"),
                (new(@"^Enumerating objects: (.+)$"), "Enumerating"),
                (new(@"^Counting objects: (.+)$"), "Counting"),
                (new(@"^Compressing objects: (.+)$"), "Compressing"),
                (new(@"^Receiving objects: (.+)$"), "Receiving"),
                (new(@"^Writing objects: (.+)$"), "Writing"),
                (new(@"^Unpacking objects: (.+)$"), "Unpacking"),
                (new(@"^Resolving deltas: (.+)$"), "Resolving"),
                (new(@"^Updating files: (.+)$"), "UpdatingFiles"),
                (new(@"^Cloning into '(.+)'(?:\.\.\.)?$"), "Cloning"),
                (new(@"^Switched to branch '(.+)'$"), "SwitchBranch"),
                (new(@"^Switched to a new branch '(.+)'$"), "SwitchNewBranch"),
                (new(@"^Already on '(.+)'$"), "AlreadyOn"),
                (new(@"^HEAD is now at (.+)$"), "HeadAt"),
                (new(@"^Previous HEAD position was (.+)$"), "PreviousHead"),
                (new(@"^[Bb]ranch '(.+)' set up to track '(.+)'\.$"), "BranchTracking"),
                (new(@"^Your branch is up to date with '(.+)'\.$"), "BranchTrackingCurrent"),
                (new(@"^Deleted branch (.+) \(was ([0-9a-f]+)\)\.$"), "BranchDeleted"),
                (new(@"^Deleted tag '(.+)' \(was ([0-9a-f]+)\)$"), "TagDeleted"),
                (new(@"^Saved working directory and index state (.+)$"), "SavedStash"),
                (new(@"^No local changes to save$"), "NoStashChanges"),
                (new(@"^No stash entries found\.$"), "NoStashEntries"),
                (new(@"^Dropped (stash@\{\d+\} .+)$"), "DroppedStash"),
                (new(@"^fatal: not a git repository \(or any of the parent directories\): \.git$"), "NotRepository"),
                (new(@"^fatal: detected dubious ownership in repository at '(.+)'$"), "DubiousOwnership"),
                (new(@"^fatal: unable to access '(.+)': (.+)$"), "CannotAccess"),
                (new(@"^fatal: Authentication failed for '(.+)'$"), "Authentication"),
                (new(@"^fatal: Could not read from remote repository\.$"), "RemoteRead"),
                (new(@"^Please make sure you have the correct access rights$"), "RemoteReadHint"),
                (new(@"^and the repository exists\.$"), "RemoteExistsHint"),
                (new(@"^fatal: couldn't find remote ref (.+)$"), "RemoteRef"),
                (new(@"^error: failed to push some refs to '(.+)'$"), "PushFailed"),
                (new(@"^There is no tracking information for the current branch\.$"), "NoTracking"),
                (new(@"^fatal: The current branch (.+) has no upstream branch\.$"), "NoTrackingPush"),
                (new(@"^fatal: Need to specify how to reconcile divergent branches\.$"), "PullDivergent"),
                (new(@"^error: Your local changes to the following files would be overwritten by (merge|checkout):$"), "LocalChangesOverwrite"),
                (new(@"^error: The following untracked working tree files would be overwritten by (merge|checkout):$"), "UntrackedOverwrite"),
                (new(@"^Please commit your changes or stash them before you (?:merge|switch branches)\.$"), "StashBeforeMerge"),
                (new(@"^Aborting$"), "Aborting"),
                (new(@"^hint: Resolve all conflicts manually, mark them as resolved with$"), "HintResolve"),
                (new(@"^hint: ""git add/rm <conflicted_files>"", then run ""git rebase --continue""\.$"), "HintContinue"),
                (new(@"^hint: You can instead skip this commit: run ""git rebase --skip""\.$"), "HintSkip"),
                (new(@"^hint: To abort and get back to the state before ""git rebase"", run ""git rebase --abort""\.$"), "HintAbort"),
                (new(@"^hint: Disable this message with ""(.+)""$"), "HintDisable"),
                (new(@"^hint: After resolving the conflicts, mark them with$"), "HintConflictAdd"),
                (new(@"^hint:\s+(""?git add/rm <pathspec>""?)(?:,)?$"), "HintConflictPath"),
                (new(@"^hint:\s+then run ""(.+)""\.$"), "HintThenRun"),
            ];
        }
    }
}
