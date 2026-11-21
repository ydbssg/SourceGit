using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RepositoryToolbar : UserControl
    {
        public RepositoryToolbar()
        {
            InitializeComponent();
        }

        private void OpenWithExternalTools(object sender, RoutedEventArgs ev)
        {
            if (sender is Button button && DataContext is ViewModels.Repository repo)
            {
                var fullpath = repo.FullPath;
                if (!Directory.Exists(fullpath))
                    return;

                var isMacOS = OperatingSystem.IsMacOS();
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                RenderOptions.SetBitmapInterpolationMode(menu, BitmapInterpolationMode.HighQuality);
                RenderOptions.SetEdgeMode(menu, EdgeMode.Antialias);
                RenderOptions.SetTextRenderingMode(menu, TextRenderingMode.SubpixelAntialias);

                var explore = new MenuItem();
                explore.Header = App.Text("Repository.Explore");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.Tag = isMacOS ? "⌘+E" : "Ctrl+E";
                explore.Click += (_, e) =>
                {
                    Native.OS.OpenInFileManager(fullpath);
                    e.Handled = true;
                };

                var terminal = new MenuItem();
                terminal.Header = App.Text("Repository.Terminal");
                terminal.Icon = this.CreateMenuIcon("Icons.Terminal");
                terminal.Tag = isMacOS ? "Λ+`" : "Ctrl+`";
                terminal.Click += (_, e) =>
                {
                    Native.OS.OpenTerminal(fullpath);
                    e.Handled = true;
                };

                menu.Items.Add(explore);
                menu.Items.Add(terminal);

                var tools = Native.OS.ExternalTools;
                if (tools.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var tool in tools)
                    {
                        var dupTool = tool;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.OpenIn", dupTool.Name);
                        item.Icon = new Image { Width = 16, Height = 16, Source = dupTool.IconImage };

                        var options = dupTool.MakeLaunchOptions(fullpath);
                        var count = (dupTool.SupportOpenFolder ? 1 : 0) + (options?.Count ?? 0);
                        if (count == 0)
                            continue;

                        if (count == 1)
                        {
                            var args = fullpath.Quoted();
                            if (options is { Count: 1 })
                                args = options[0].Args;

                            item.Click += (_, e) =>
                            {
                                dupTool.Launch(args);
                                e.Handled = true;
                            };
                        }
                        else
                        {
                            foreach (var opt in options)
                            {
                                var subItem = new MenuItem();
                                subItem.Header = opt.Title;
                                subItem.Click += (_, e) =>
                                {
                                    dupTool.Launch(opt.Args);
                                    e.Handled = true;
                                };

                                item.Items.Add(subItem);
                            }

                            if (dupTool.SupportOpenFolder)
                            {
                                var open = new MenuItem();
                                open.Header = App.Text("Repository.OpenAsFolder");
                                open.Click += (_, e) =>
                                {
                                    dupTool.Launch(fullpath.Quoted());
                                    e.Handled = true;
                                };
                                item.Items.Add(new MenuItem() { Header = "-" });
                                item.Items.Add(open);
                            }
                        }

                        menu.Items.Add(item);
                    }
                }

                var urls = new Dictionary<string, string>();
                foreach (var r in repo.Remotes)
                {
                    if (r.TryGetVisitURL(out var visit))
                        urls.Add(r.Name, visit);
                }

                if (urls.Count > 0)
                {
                    menu.Items.Add(new MenuItem() { Header = "-" });

                    foreach (var (name, addr) in urls)
                    {
                        var dupUrl = addr;

                        var item = new MenuItem();
                        item.Header = App.Text("Repository.Visit", name);
                        item.Icon = this.CreateMenuIcon("Icons.Remotes");
                        item.Click += (_, e) =>
                        {
                            Native.OS.OpenBrowser(dupUrl);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }

                menu.Open(button);
                ev.Handled = true;
            }
        }

        private async void OpenStatistics(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await this.ShowDialogAsync(new ViewModels.Statistics(repo.FullPath));
                e.Handled = true;
            }
        }

        private async void FetchDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.FetchAsync(true);
                e.Handled = true;
            }
        }

        private async void Pull(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void PullDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PullAsync(true);
                e.Handled = true;
            }
        }

        private async void Push(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(e.KeyModifiers is KeyModifiers.Control);
                e.Handled = true;
            }
        }

        private async void PushDirectlyByHotKey(object sender, RoutedEventArgs e)
        {
            if (App.GetLauncher() is { CommandPalette: { } } launcher)
                return;

            if (DataContext is ViewModels.Repository repo)
            {
                await repo.PushAsync(true);
                e.Handled = true;
            }
        }

        private async void OpenMergeRequest(object _, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository { IsDashboardVisible: true } repo || !repo.CanCreatePopup())
                return;

            e.Handled = true;
            if (repo.Histories?.SelectedCommits is not { Count: > 0 } selected)
            {
                repo.SendNotification(App.Text("CreateMergeRequest.SelectCommits"), true);
                return;
            }

            if (repo.Remotes.Count == 0)
            {
                repo.SendNotification(App.Text("CreateMergeRequest.NoRemotes"), true);
                return;
            }

            var selectedIds = selected.Select(commit => commit.SHA).ToHashSet(StringComparer.Ordinal);
            // The history is shown newest first in topological or date order.
            var commits = repo.Histories.Commits.Where(commit => selectedIds.Contains(commit.SHA)).Reverse().ToList();
            if (commits.Count != selectedIds.Count)
            {
                repo.SendNotification(App.Text("CreateMergeRequest.SelectCommits"), true);
                return;
            }

            if (commits.Exists(commit => commit.Parents.Count > 1))
            {
                repo.SendNotification(App.Text("CreateMergeRequest.MergeCommit"), true);
                return;
            }

            var dialog = new CreateMergeRequest { DataContext = new ViewModels.CreateMergeRequest(repo, commits) };
            await this.ShowDialogAsync(dialog);
        }

        private void OpenRemote(object _, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            e.Handled = true;
            foreach (var remote in repo.Remotes.OrderByDescending(remote => remote.Name == "origin"))
            {
                if (remote.TryGetVisitURL(out var url))
                {
                    Native.OS.OpenBrowser(url);
                    return;
                }
            }

            repo.SendNotification(App.Text("Repository.Toolbar.NoWebRemote"), true);
        }

        private void BrowseRepository(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && Directory.Exists(repo.FullPath))
            {
                Native.OS.OpenInFileManager(repo.FullPath);
                e.Handled = true;
            }
        }

        private void OpenMoreMenu(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository repo || sender is not Button button)
                return;

            var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
            var fetch = CreateToolbarMenuItem("Repository.Toolbar.Fetch", "Icons.Fetch");
            ToolTip.SetTip(fetch, App.Text("CtrlClickTip"));
            var fetchWithDefaults = false;
            fetch.AddHandler(PointerReleasedEvent, (_, args) => fetchWithDefaults = args.KeyModifiers is KeyModifiers.Control, RoutingStrategies.Tunnel);
            fetch.Click += async (_, args) =>
            {
                args.Handled = true;
                await repo.FetchAsync(fetchWithDefaults);
            };
            menu.Items.Add(fetch);

            if (!repo.IsBare)
            {
                var stash = CreateToolbarMenuItem("Stash", "Icons.Stashes.Add");
                ToolTip.SetTip(stash, App.Text("CtrlClickTip"));
                var stashWithDefaults = false;
                stash.AddHandler(PointerReleasedEvent, (_, args) => stashWithDefaults = args.KeyModifiers is KeyModifiers.Control, RoutingStrategies.Tunnel);
                stash.Click += async (_, args) =>
                {
                    args.Handled = true;
                    await repo.StashAllAsync(stashWithDefaults);
                };
                menu.Items.Add(stash);

                var apply = CreateToolbarMenuItem("Apply.Title", "Icons.ApplyPatch");
                apply.Click += (_, args) =>
                {
                    repo.ApplyPatch();
                    args.Handled = true;
                };
                menu.Items.Add(apply);

                menu.Items.Add(CreateGitFlowMenu(repo));
                menu.Items.Add(CreateGitLFSMenu(repo));

                var bisect = CreateToolbarMenuItem("Bisect", "Icons.Bisect");
                bisect.Click += StartBisect;
                menu.Items.Add(bisect);
                menu.Items.Add(new MenuItem { Header = "-" });
            }

            var cleanup = CreateToolbarMenuItem("Repository.Clean", "Icons.Clean");
            cleanup.Click += Cleanup;
            menu.Items.Add(cleanup);

            menu.Open(button);
            e.Handled = true;
        }

        private MenuItem CreateToolbarMenuItem(string textKey, string iconKey)
        {
            return new MenuItem { Header = App.Text(textKey), Icon = this.CreateMenuIcon(iconKey) };
        }

        private MenuItem CreateGitFlowMenu(ViewModels.Repository repo)
        {
            var menu = CreateToolbarMenuItem("GitFlow", "Icons.GitFlow");

            if (repo.IsGitFlowEnabled())
            {
                var startFeature = new MenuItem();
                startFeature.Header = App.Text("GitFlow.StartFeature");
                startFeature.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Feature));
                    e.Handled = true;
                };

                var startRelease = new MenuItem();
                startRelease.Header = App.Text("GitFlow.StartRelease");
                startRelease.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Release));
                    e.Handled = true;
                };

                var startHotfix = new MenuItem();
                startHotfix.Header = App.Text("GitFlow.StartHotfix");
                startHotfix.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.GitFlowStart(repo, Models.GitFlowBranchType.Hotfix));
                    e.Handled = true;
                };

                menu.Items.Add(startFeature);
                menu.Items.Add(startRelease);
                menu.Items.Add(startHotfix);

                var type = repo.CurrentBranch != null ? repo.GetGitFlowType(repo.CurrentBranch) : Models.GitFlowBranchType.None;
                if (type != Models.GitFlowBranchType.None)
                {
                    var finish = new MenuItem();
                    finish.Header = App.Text("GitFlow.Finish", repo.CurrentBranch.Name);
                    finish.Icon = this.CreateMenuIcon("Icons.GitFlow.Finish");
                    finish.Click += (_, e) =>
                    {
                        if (repo.CanCreatePopup())
                            repo.ShowPopup(new ViewModels.GitFlowFinish(repo, repo.CurrentBranch, type));
                        e.Handled = true;
                    };
                    menu.Items.Add(new MenuItem() { Header = "-" });
                    menu.Items.Add(finish);
                }
            }
            else
            {
                var init = new MenuItem();
                init.Header = App.Text("GitFlow.Init");
                init.Icon = this.CreateMenuIcon("Icons.Init");
                init.Click += (_, e) =>
                {
                    if (repo.CurrentBranch == null)
                        repo.SendNotification(App.Text("Diagnostics.NoGitFlowBranch"), true);
                    else if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.InitGitFlow(repo));

                    e.Handled = true;
                };
                menu.Items.Add(init);
            }

            return menu;
        }

        private MenuItem CreateGitLFSMenu(ViewModels.Repository repo)
        {
            var menu = CreateToolbarMenuItem("GitLFS", "Icons.LFS");

            if (repo.IsLFSEnabled())
            {
                var addPattern = new MenuItem();
                addPattern.Header = App.Text("GitLFS.AddTrackPattern");
                addPattern.Icon = this.CreateMenuIcon("Icons.File.Add");
                addPattern.Click += (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        repo.ShowPopup(new ViewModels.LFSTrackCustomPattern(repo));

                    e.Handled = true;
                };
                menu.Items.Add(addPattern);
                menu.Items.Add(new MenuItem() { Header = "-" });

                var fetch = new MenuItem();
                fetch.Header = App.Text("GitLFS.Fetch");
                fetch.Icon = this.CreateMenuIcon("Icons.Fetch");
                fetch.IsEnabled = repo.Remotes.Count > 0;
                fetch.Click += async (_, e) =>
                {
                    if (repo.CanCreatePopup())
                    {
                        if (repo.Remotes.Count == 1)
                            await repo.ShowAndStartPopupAsync(new ViewModels.LFSFetch(repo));
                        else
                            repo.ShowPopup(new ViewModels.LFSFetch(repo));
                    }

                    e.Handled = true;
                };
                menu.Items.Add(fetch);

                var pull = new MenuItem();
                pull.Header = App.Text("GitLFS.Pull");
                pull.Icon = this.CreateMenuIcon("Icons.Pull");
                pull.IsEnabled = repo.Remotes.Count > 0;
                pull.Click += async (_, e) =>
                {
                    if (repo.CanCreatePopup())
                    {
                        if (repo.Remotes.Count == 1)
                            await repo.ShowAndStartPopupAsync(new ViewModels.LFSPull(repo));
                        else
                            repo.ShowPopup(new ViewModels.LFSPull(repo));
                    }

                    e.Handled = true;
                };
                menu.Items.Add(pull);

                var push = new MenuItem();
                push.Header = App.Text("GitLFS.Push");
                push.Icon = this.CreateMenuIcon("Icons.Push");
                push.IsEnabled = repo.Remotes.Count > 0;
                push.Click += async (_, e) =>
                {
                    if (repo.CanCreatePopup())
                    {
                        if (repo.Remotes.Count == 1)
                            await repo.ShowAndStartPopupAsync(new ViewModels.LFSPush(repo));
                        else
                            repo.ShowPopup(new ViewModels.LFSPush(repo));
                    }

                    e.Handled = true;
                };
                menu.Items.Add(push);

                var prune = new MenuItem();
                prune.Header = App.Text("GitLFS.Prune");
                prune.Icon = this.CreateMenuIcon("Icons.Clean");
                prune.Click += async (_, e) =>
                {
                    if (repo.CanCreatePopup())
                        await repo.ShowAndStartPopupAsync(new ViewModels.LFSPrune(repo));

                    e.Handled = true;
                };
                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(prune);

                var locks = new MenuItem();
                locks.Header = App.Text("GitLFS.Locks");
                locks.Icon = this.CreateMenuIcon("Icons.Lock");
                locks.IsEnabled = repo.Remotes.Count > 0;
                if (repo.Remotes.Count == 1)
                {
                    locks.Click += async (_, e) =>
                    {
                        await this.ShowDialogAsync(new ViewModels.LFSLocks(repo, repo.Remotes[0].Name));
                        e.Handled = true;
                    };
                }
                else
                {
                    foreach (var remote in repo.Remotes)
                    {
                        var remoteName = remote.Name;
                        var lockRemote = new MenuItem();
                        lockRemote.Header = remoteName;
                        lockRemote.Click += async (_, e) =>
                        {
                            await this.ShowDialogAsync(new ViewModels.LFSLocks(repo, remoteName));
                            e.Handled = true;
                        };
                        locks.Items.Add(lockRemote);
                    }
                }

                menu.Items.Add(new MenuItem() { Header = "-" });
                menu.Items.Add(locks);
            }
            else
            {
                var install = new MenuItem();
                install.Header = App.Text("GitLFS.Install");
                install.Icon = this.CreateMenuIcon("Icons.Init");
                install.Click += async (_, e) =>
                {
                    await repo.InstallLFSAsync();
                    e.Handled = true;
                };
                menu.Items.Add(install);
            }

            return menu;
        }

        private async void StartBisect(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { IsBisectCommandRunning: false, InProgressContext: null } repo &&
                repo.CanCreatePopup())
            {
                if (repo.LocalChangesCount > 0)
                    repo.SendNotification(App.Text("Diagnostics.DirtyWorktree"), true);
                else if (repo.IsBisectCommandRunning || repo.BisectState != Models.BisectState.None)
                    repo.SendNotification(App.Text("Diagnostics.BisectRunning"), true);
                else
                    await repo.ExecBisectCommandAsync("start");
            }

            e.Handled = true;
        }

        private void OpenCustomActionMenu(object sender, RoutedEventArgs ev)
        {
            if (DataContext is ViewModels.Repository repo && sender is Control control)
            {
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;

                var actions = repo.GetCustomActions(Models.CustomActionScope.Repository);
                if (actions.Count > 0)
                {
                    foreach (var action in actions)
                    {
                        var (dup, label) = action;
                        var item = new MenuItem();
                        item.Icon = this.CreateMenuIcon("Icons.Action");
                        item.Header = label;
                        item.Click += async (_, e) =>
                        {
                            await repo.ExecCustomActionAsync(dup, null);
                            e.Handled = true;
                        };

                        menu.Items.Add(item);
                    }
                }
                else
                {
                    menu.Items.Add(new MenuItem() { Header = App.Text("Repository.CustomActions.Empty") });
                }

                menu.Open(control);
            }

            ev.Handled = true;
        }

        private async void OpenGitLogs(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await this.ShowDialogAsync(new ViewModels.ViewLogs(repo));
                e.Handled = true;
            }
        }

        private async void Cleanup(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo)
            {
                await repo.CleanupAsync();
                e.Handled = true;
            }
        }

        private void NavigateToHead(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository { CurrentBranch: { } head } repo)
            {
                var repoView = TopLevel.GetTopLevel(this)?.FindDescendantOfType<Repository>();
                repoView?.LocalBranchTree?.Select(head);

                repo.NavigateToCommit(head.Head);
                e.Handled = true;
            }
        }
    }
}
