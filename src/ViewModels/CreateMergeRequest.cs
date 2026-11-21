using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public sealed class CreateMergeRequest : ObservableObject
    {
        private const string BranchPrefix = "refs/heads/";

        public List<Models.Remote> Remotes { get; }
        public IReadOnlyList<Models.Commit> SelectedCommits => _commits;
        public string SelectedCommitsLabel => App.Text("CreateMergeRequest.Selected", _commits.Count);

        public Models.Remote SelectedRemote
        {
            get => _selectedRemote;
            set => SetProperty(ref _selectedRemote, value);
        }

        public List<string> TargetBranches
        {
            get => _targetBranches;
            private set => SetProperty(ref _targetBranches, value);
        }

        public string TargetBranch
        {
            get => _targetBranch;
            set
            {
                if (SetProperty(ref _targetBranch, value) && !string.IsNullOrEmpty(value))
                    SourceBranch = BuildSourceBranch(value);
                OnPropertyChanged(nameof(CanPublish));
            }
        }

        public string SourceBranch
        {
            get => _sourceBranch;
            set => SetProperty(ref _sourceBranch, value);
        }

        public string RequestTitle
        {
            get => _requestTitle;
            set => SetProperty(ref _requestTitle, value);
        }

        public bool CreateOnGitLab
        {
            get => _createOnGitLab;
            set
            {
                if (SetProperty(ref _createOnGitLab, value))
                    OnPropertyChanged(nameof(PublishLabel));
            }
        }

        public string PublishLabel => App.Text(CreateOnGitLab ? "CreateMergeRequest.Publish" : "CreateMergeRequest.PushSource");

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string Details
        {
            get => _details;
            private set
            {
                if (SetProperty(ref _details, value))
                    OnPropertyChanged(nameof(HasDetails));
            }
        }

        public bool HasDetails => !string.IsNullOrEmpty(Details);

        public bool CanEditRequest => !IsBusy && !_published;
        public bool CanChooseTarget => CanEditRequest;
        public bool CanPublish => CanEditRequest && SelectedRemote != null && TargetBranch != null;
        public bool ShowPublish => !_published;
        public bool HasBrowserLink => !string.IsNullOrEmpty(_browserUrl);
        public string BrowserLinkLabel => App.Text(_hasMergeRequestUrl ? "CreateMergeRequest.OpenRequest" : "CreateMergeRequest.OpenWebForm");

        public CreateMergeRequest(Repository repo, List<Models.Commit> commits)
        {
            _repo = repo;
            _commits = commits;
            Remotes = [.. repo.Remotes];
            _selectedRemote = Remotes.Find(remote => remote.Name == "origin") ?? Remotes[0];
            _createOnGitLab = repo.CreateMergeRequestOnGitLab;
            _requestTitle = commits[^1].Subject;
        }

        public Task LoadTargetsAsync()
        {
            if (!CanChooseTarget || SelectedRemote == null)
                return Task.CompletedTask;

            var remote = SelectedRemote;
            var previous = TargetBranch;
            var previousSource = SourceBranch;
            return RunOperationAsync("LoadingBranches", async () =>
            {
                TargetBranch = null;
                TargetBranches = [];
                var result = await RequireGitAsync("ls-remote", "--refs", "--", remote.Name, "refs/heads/*");
                TargetBranches = Commands.MergeRequestGit.ParseRemoteRefs(result.StdOut).Keys
                    .Where(reference => reference.StartsWith(BranchPrefix, StringComparison.Ordinal))
                    .Select(reference => reference[BranchPrefix.Length..])
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                var upstreamPrefix = $"refs/remotes/{remote.Name}/";
                var upstream = _repo.CurrentBranch?.Upstream;
                var upstreamBranch = upstream?.StartsWith(upstreamPrefix, StringComparison.Ordinal) == true
                    ? upstream[upstreamPrefix.Length..]
                    : null;
                TargetBranch = TargetBranches.Find(name => name == previous)
                    ?? TargetBranches.Find(name => name == "release")
                    ?? TargetBranches.Find(name => name == upstreamBranch);
                if (TargetBranch != null && TargetBranch == previous)
                    SourceBranch = previousSource;
                Status = App.Text(TargetBranches.Count == 0 ? "CreateMergeRequest.NoTargets" : "CreateMergeRequest.ChooseTarget");
            });
        }

        public Task PublishAsync()
        {
            if (!CanPublish)
                return Task.CompletedTask;

            return RunOperationAsync("Preparing", async () =>
            {
                if (Native.OS.GitVersion < Models.GitVersions.MERGE_TREE_EXPLICIT_BASE)
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.GitVersionRequired"));

                await ValidateSourceBranchAsync();
                var title = RequestTitle?.Trim();
                if (CreateOnGitLab && (string.IsNullOrEmpty(title) || title.IndexOfAny(['\r', '\n', '\0']) >= 0))
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.InvalidTitle"));

                var pushUrls = await RequireGitAsync("remote", "get-url", "--push", "--all", "--", SelectedRemote.Name);
                var destinations = pushUrls.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (destinations.Length != 1)
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.MultiplePushUrls"));

                var url = destinations[0];
                var sourceRef = BranchPrefix + SourceBranch.Trim();
                var targetRef = BranchPrefix + TargetBranch;
                var before = await ReadDestinationAsync(url, sourceRef, targetRef);
                if (before.TryGetValue(sourceRef, out var existing))
                {
                    if (_attemptedPush == new PushAttempt(url, sourceRef, targetRef, existing))
                    {
                        FinishPublish(null, url);
                        return;
                    }

                    throw new InvalidOperationException(App.Text("CreateMergeRequest.SourceExists", SourceBranch));
                }
                if (!before.TryGetValue(targetRef, out var baseline))
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.TargetChanged"));

                var localCommit = await RunGitAsync("cat-file", "-e", $"{baseline}^{{commit}}");
                var scratchRef = localCommit.IsSuccess ? null : $"refs/sourcegit/merge-request/{Guid.NewGuid():N}";
                try
                {
                    if (scratchRef != null)
                    {
                        Status = App.Text("CreateMergeRequest.FetchingTarget", SelectedRemote.Name, TargetBranch);
                        await RequireGitAsync("fetch", "--no-tags", "--no-prune", "--no-prune-tags", "--no-recurse-submodules",
                            "--refmap=", "--no-write-fetch-head", "--", SelectedRemote.Name, $"{targetRef}:{scratchRef}");
                        var fetched = await RequireGitAsync("rev-parse", "--verify", $"{scratchRef}^{{commit}}");
                        if (fetched.StdOut.Trim() != baseline)
                            throw new InvalidOperationException(App.Text("CreateMergeRequest.TargetChanged"));
                    }

                    var head = await ApplySelectedCommitsAsync(baseline);
                    if (head == null)
                        return;

                    var latest = await ReadDestinationAsync(url, sourceRef, targetRef);
                    if (!latest.TryGetValue(targetRef, out var current) || current != baseline)
                        throw new InvalidOperationException(App.Text("CreateMergeRequest.TargetChanged"));
                    if (latest.ContainsKey(sourceRef))
                        throw new InvalidOperationException(App.Text("CreateMergeRequest.SourceExists", SourceBranch));

                    await PushAsync(url, sourceRef, targetRef, head, title);
                }
                finally
                {
                    if (scratchRef != null)
                        await RunGitAsync("update-ref", "-d", scratchRef);
                }
            });
        }

        private async Task PushAsync(string url, string sourceRef, string targetRef, string head, string title)
        {
            Status = App.Text("CreateMergeRequest.Publishing");
            var push = new List<string>
            {
                "-c", $"remote.{SelectedRemote.Name}.mirror=false", "-c", "push.pushOption=",
                "push", "--porcelain", "--no-follow-tags", "--recurse-submodules=no",
                $"--force-with-lease={sourceRef}:",
            };
            if (CreateOnGitLab)
            {
                push.Add("--push-option=merge_request.create");
                push.Add($"--push-option=merge_request.target={TargetBranch}");
                push.Add($"--push-option=merge_request.title={title}");
            }
            push.AddRange(["--", SelectedRemote.Name, $"{head}:{sourceRef}"]);

            _repo.CreateMergeRequestOnGitLab = CreateOnGitLab;
            _attemptedPush = new PushAttempt(url, sourceRef, targetRef, head);
            var pushed = await RunGitAsync([.. push]);
            if (!pushed.IsSuccess)
            {
                var after = await RunGitAsync("ls-remote", "--refs", "--", url, sourceRef);
                var refs = Commands.MergeRequestGit.ParseRemoteRefs(after.StdOut);
                if (!after.IsSuccess || !refs.TryGetValue(sourceRef, out var found) || found != head)
                    throw new InvalidOperationException(ReadError(pushed));
            }

            FinishPublish(pushed, url);
        }

        public void OpenBrowserLink()
        {
            if (HasBrowserLink)
                Native.OS.OpenBrowser(_browserUrl);
        }

        private async Task<string> ApplySelectedCommitsAsync(string baseline)
        {
            var baseTree = (await RequireGitAsync("rev-parse", "--verify", $"{baseline}^{{tree}}")).StdOut.Trim();
            var signing = await RunGitAsync("config", "--type=bool", "--get", "commit.gpgSign");
            if (signing.ExitCode is not (0 or 1))
                throw new InvalidOperationException(ReadError(signing));
            var sign = signing.StdOut.Trim() == "true";

            var head = baseline;
            var tree = baseTree;
            for (var i = 0; i < _commits.Count; i++)
            {
                var commit = _commits[i];
                Status = App.Text("CreateMergeRequest.Applying", i + 1, _commits.Count, commit.Subject);
                var ancestor = await RunGitAsync("merge-base", "--is-ancestor", commit.SHA, baseline);
                if (ancestor.IsSuccess)
                    continue;
                if (ancestor.ExitCode != 1)
                    throw new InvalidOperationException(ReadError(ancestor));

                var original = await RequireGitAsync("show", "-s", "--no-notes", "--no-show-signature", "--encoding=UTF-8",
                    "--format=%P%x00%an%x00%ae%x00%aI%x00%B", commit.SHA, "--");
                var metadata = original.StdOut.Split('\0', 5);
                if (metadata.Length != 5)
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.InvalidCommitMetadata", commit.SHA[..10]));
                var parents = metadata[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parents.Length > 1)
                    throw new InvalidOperationException(App.Text("CreateMergeRequest.MergeCommit"));

                // The selected commit's parent defines its patch. An empty merge base applies the root commit's whole tree.
                var mergeBase = parents.Length == 0 ? "--allow-unrelated-histories" : $"--merge-base={parents[0]}";
                var merged = await RunGitAsync("merge-tree", "--write-tree", "--name-only", mergeBase, head, commit.SHA);
                if (merged.ExitCode == 1)
                {
                    var output = merged.StdOut.TrimEnd();
                    var firstLine = output.IndexOf('\n');
                    Details = firstLine < 0 ? null : Models.GitOutputText.TranslateMessage(output[(firstLine + 1)..].Trim());
                    Status = App.Text("CreateMergeRequest.ConflictStopped", commit.SHA[..10], commit.Subject);
                    _log.AppendLine(Status);
                    if (HasDetails)
                        _log.AppendLine(Details);
                    return null;
                }
                if (!merged.IsSuccess)
                    throw new InvalidOperationException(ReadError(merged));

                var newTree = merged.StdOut.Trim();
                if (newTree == tree)
                    continue;

                var message = metadata[4].TrimEnd('\r', '\n') + $"\n\n(cherry picked from commit {commit.SHA})\n";
                var arguments = new List<string> { "commit-tree", newTree, "-p", head };
                if (sign)
                    arguments.Add("-S");

                _log.AppendLine("$ git commit-tree");
                var created = await new Commands.MergeRequestGit(_repo.FullPath, _repo.FullPath, SelectedRemote, [.. arguments])
                    .WriteCommitAsync(metadata[1], metadata[2], metadata[3], message);
                if (!created.IsSuccess)
                    throw new InvalidOperationException(ReadError(created));

                head = created.StdOut.Trim();
                tree = newTree;
            }

            if (tree != baseTree)
                return head;

            Status = App.Text("CreateMergeRequest.NoChanges");
            _log.AppendLine(Status);
            return null;
        }

        private async Task ValidateSourceBranchAsync()
        {
            var source = SourceBranch?.Trim();
            if (string.IsNullOrEmpty(source) || source == TargetBranch || source.StartsWith('-'))
                throw new InvalidOperationException(App.Text("CreateMergeRequest.InvalidSource"));

            var result = await RunGitAsync("check-ref-format", BranchPrefix + source);
            if (!result.IsSuccess)
                throw new InvalidOperationException(App.Text("CreateMergeRequest.InvalidSource"));
        }

        private async Task<Dictionary<string, string>> ReadDestinationAsync(string url, string sourceRef, string targetRef)
        {
            var result = await RequireGitAsync("ls-remote", "--refs", "--", url, sourceRef, targetRef);
            return Commands.MergeRequestGit.ParseRemoteRefs(result.StdOut);
        }

        private void FinishPublish(Commands.Command.Result result, string pushUrl)
        {
            _published = true;
            var lines = result == null ? [] : (result.StdOut + "\n" + result.StdErr).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            _browserUrl = lines.FirstOrDefault(IsMergeRequestUrl);
            _hasMergeRequestUrl = _browserUrl != null;
            _browserUrl ??= BuildGitLabWebForm(pushUrl);
            Status = App.Text(_hasMergeRequestUrl ? "CreateMergeRequest.Created" : "CreateMergeRequest.Pushed", SourceBranch.Trim());
            if (result != null && !result.IsSuccess)
                Details = ReadError(result);
        }

        private string BuildGitLabWebForm(string pushUrl)
        {
            string projectUrl;
            try
            {
                if (!new Models.Remote { URL = pushUrl }.TryGetVisitURL(out projectUrl) &&
                    !SelectedRemote.TryGetVisitURL(out projectUrl))
                    return null;
            }
            catch (UriFormatException)
            {
                return null;
            }

            if (!TryParseWebUrl(projectUrl, out var uri))
                return null;

            var source = Uri.EscapeDataString(SourceBranch.Trim());
            var target = Uri.EscapeDataString(TargetBranch);
            return $"{uri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/-/merge_requests/new?merge_request%5Bsource_branch%5D={source}&merge_request%5Btarget_branch%5D={target}";
        }

        private static bool IsMergeRequestUrl(string text)
        {
            if (!TryParseWebUrl(text, out var uri))
                return false;

            var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
            return parts.Length > 2 && parts[^2] == "merge_requests" && int.TryParse(parts[^1], out var number) && number > 0;
        }

        private static bool TryParseWebUrl(string text, out Uri uri)
        {
            return Uri.TryCreate(text, UriKind.Absolute, out uri) &&
                uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
        }

        private async Task RunOperationAsync(string statusKey, Func<Task> operation)
        {
            IsBusy = true;
            Status = App.Text($"CreateMergeRequest.{statusKey}");
            Details = null;
            RefreshActions();
            _log = _repo.CreateLog(App.Text("CreateMergeRequest.Title"));
            try
            {
                await operation();
            }
            catch (Exception e)
            {
                Status = App.Text(_published ? "CreateMergeRequest.PublishedLocalFailure" : "CreateMergeRequest.Failed");
                Details = e.Message;
                _log.AppendLine(e.Message);
            }
            finally
            {
                _log.Complete();
                _log = null;
                IsBusy = false;
                RefreshActions();
            }
        }

        private async Task<Commands.Command.Result> RunGitAsync(params string[] arguments)
        {
            var command = new Commands.MergeRequestGit(_repo.FullPath, _repo.FullPath, SelectedRemote, arguments);
            // Remote URLs can contain credentials, so log the command name only.
            _log.AppendLine($"$ git {(arguments[0] == "-c" ? "push" : arguments[0])}");
            var result = await command.RunAsync();
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                _log.AppendLine(Models.GitOutputText.TranslateMessage(result.StdErr.Trim()));
            return result;
        }

        private async Task<Commands.Command.Result> RequireGitAsync(params string[] arguments)
        {
            var result = await RunGitAsync(arguments);
            if (!result.IsSuccess)
                throw new InvalidOperationException(ReadError(result));
            return result;
        }

        private static string ReadError(Commands.Command.Result result)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            return string.IsNullOrWhiteSpace(error)
                ? App.Text("CreateMergeRequest.GitFailed")
                : Models.GitOutputText.TranslateMessage(error.Trim());
        }

        private void RefreshActions()
        {
            OnPropertyChanged(nameof(CanChooseTarget));
            OnPropertyChanged(nameof(CanEditRequest));
            OnPropertyChanged(nameof(ShowPublish));
            OnPropertyChanged(nameof(CanPublish));
            OnPropertyChanged(nameof(HasBrowserLink));
            OnPropertyChanged(nameof(BrowserLinkLabel));
        }

        private static string BuildSourceBranch(string target)
        {
            var user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            var prefix = target.Replace('/', '-');
            var suffix = Guid.NewGuid().ToString("N")[..8];
            return $"{prefix[..Math.Min(prefix.Length, 64)]}_{(user.Length > 0 ? user[..Math.Min(user.Length, 20)] : "mr")}_{suffix}";
        }

        private sealed record PushAttempt(string Url, string Source, string Target, string Head);

        private readonly Repository _repo;
        private readonly List<Models.Commit> _commits;
        private Models.Remote _selectedRemote;
        private List<string> _targetBranches = [];
        private string _targetBranch;
        private string _sourceBranch;
        private string _requestTitle;
        private bool _createOnGitLab;
        private bool _isBusy;
        private string _status;
        private string _details;
        private bool _published;
        private PushAttempt _attemptedPush;
        private string _browserUrl;
        private bool _hasMergeRequestUrl;
        private CommandLog _log;
    }
}
