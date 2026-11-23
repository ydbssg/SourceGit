using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using SourceGit.Models;

namespace SourceGit.ViewModels
{
    internal enum SpreadsheetPullResult
    {
        NotApplicable,
        Completed,
        Paused,
        Aborted,
    }

    internal static class SpreadsheetPullConflict
    {
        public static bool HasPendingOperation(Repository repo) => GetOperation(repo) != null;

        public static async Task<SpreadsheetPullResult> ResolveAsync(Repository repo, CommandLog log,
            CancellationToken cancellation, string selectedFile = null)
        {
            var handled = false;
            var previous = string.Empty;
            while (GetOperation(repo) is { } operation)
            {
                cancellation.ThrowIfCancellationRequested();
                var snapshot = await Commands.ReadRepositoryFile.ReadUnmergedAsync(repo.FullPath, cancellation);
                if (snapshot == previous)
                    break;

                previous = snapshot;
                var files = ReadStages(snapshot);
                var spreadsheets = files.Where(pair => SpreadsheetMergePlan.Supports(pair.Key) &&
                    (selectedFile == null || pair.Key == selectedFile)).ToList();
                if (spreadsheets.Count == 0)
                    break;

                var prepared = new List<PendingFile>();
                try
                {
                    foreach (var (path, stages) in spreadsheets)
                    {
                        if (stages[1] == null || stages[2] == null || stages[3] == null)
                            throw new InvalidDataException(App.Text("SpreadsheetError.MissingVersions", path));
                        var localStage = operation == "rebase" ? 3 : 2;
                        var remoteStage = operation == "rebase" ? 2 : 3;
                        prepared.Add(await Task.Run(() => PrepareFileAsync(repo.FullPath, path,
                            stages[1], stages[localStage], stages[remoteStage], cancellation), cancellation));
                    }

                    var conflicts = prepared.SelectMany(static file => file.Plan.Rows)
                        .Where(static row => !row.Resolved).ToList();
                    if (conflicts.Count > 0)
                    {
                        if (App.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                            return SpreadsheetPullResult.Paused;

                        var preview = new Views.SpreadsheetMergeDialog(conflicts, selectedFile == null);
                        var approved = await preview.ShowDialog<bool?>(owner);
                        if (approved == null || (approved == false && selectedFile != null))
                            return SpreadsheetPullResult.Paused;
                        if (approved == false)
                        {
                            if (GetOperation(repo) != operation ||
                                await Commands.ReadRepositoryFile.ReadUnmergedAsync(repo.FullPath, cancellation) != snapshot)
                                return SpreadsheetPullResult.Paused;
                            return await RunGitAsync(repo, $"{operation} --abort", log)
                                ? SpreadsheetPullResult.Aborted : SpreadsheetPullResult.Paused;
                        }
                    }

                    foreach (var file in prepared)
                    {
                        file.Template.Position = 0;
                        file.Output = await Task.Run(() => SpreadsheetMergeWriter.Write(file.Template, file.Plan, cancellation), cancellation);
                    }

                    if (GetOperation(repo) != operation ||
                        await Commands.ReadRepositoryFile.ReadUnmergedAsync(repo.FullPath, cancellation) != snapshot)
                        throw new InvalidDataException(App.Text("SpreadsheetError.IndexChanged"));
                    foreach (var file in prepared)
                    {
                        if (file.WorkingHash != await HashWorkingFileAsync(file.FullPath, cancellation))
                            throw new InvalidDataException(App.Text("SpreadsheetError.WorkingFileChanged", file.Plan.Path));
                    }

                    foreach (var file in prepared)
                        await InstallFileAsync(file, cancellation);

                    if (!await StageFilesAsync(repo, prepared, log))
                        return SpreadsheetPullResult.Paused;

                    handled = true;
                    log.AppendLine(App.Text("SpreadsheetMerge.ResolvedFiles", prepared.Count));
                    if (selectedFile != null)
                        return SpreadsheetPullResult.Completed;
                    if (spreadsheets.Count != files.Count)
                        return SpreadsheetPullResult.Paused;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return SpreadsheetPullResult.Paused;
                }
                catch (Exception e)
                {
                    var message = App.Text("SpreadsheetMerge.Failed", e.Message);
                    log.AppendLine(message);
                    repo.SendNotification(message, true);
                    return SpreadsheetPullResult.Paused;
                }
                finally
                {
                    foreach (var file in prepared)
                    {
                        file.Output?.Dispose();
                        await file.Template.DisposeAsync();
                    }
                    repo.MarkWorkingCopyDirtyManually();
                }

                if (await RunGitAsync(repo, $"{operation} --continue", log) && GetOperation(repo) == null)
                    return SpreadsheetPullResult.Completed;
            }

            return handled ? SpreadsheetPullResult.Paused : SpreadsheetPullResult.NotApplicable;
        }

        private static string GetOperation(Repository repo)
        {
            if (Directory.Exists(Path.Combine(repo.GitDir, "rebase-merge")) ||
                Directory.Exists(Path.Combine(repo.GitDir, "rebase-apply")) &&
                !File.Exists(Path.Combine(repo.GitDir, "rebase-apply", "applying")))
                return "rebase";
            return File.Exists(Path.Combine(repo.GitDir, "MERGE_HEAD")) ? "merge" : null;
        }

        private static Dictionary<string, string[]> ReadStages(string data)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var entry in data.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var delimiter = entry.IndexOf('\t');
                if (delimiter < 0)
                    throw new InvalidDataException(App.Text("SpreadsheetError.InvalidIndexEntry"));
                var gitPath = entry[(delimiter + 1)..];
                var meta = entry[..delimiter].Split(' ');
                if (meta.Length != 3 || !int.TryParse(meta[2], out var stage) || stage is < 1 or > 3)
                    throw new InvalidDataException(App.Text("SpreadsheetError.InvalidIndexStage"));

                if (!result.TryGetValue(gitPath, out var stages))
                    result.Add(gitPath, stages = new string[4]);
                if (meta[0] is "100644" or "100755")
                    stages[stage] = meta[1];
            }
            return result;
        }

        private static async Task<PendingFile> PrepareFileAsync(string repo, string path, string baseline,
            string local, string remote, CancellationToken cancellation)
        {
            var fullPath = Path.GetFullPath(path, repo);
            var relative = Path.GetRelativePath(repo, fullPath);
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar)[0] == ".." ||
                File.Exists(fullPath) && new FileInfo(fullPath).LinkTarget != null)
                throw new InvalidDataException(App.Text("SpreadsheetError.InvalidPath", path));

            var workingHash = await HashWorkingFileAsync(fullPath, cancellation).ConfigureAwait(false);
            await using var baseStream = await Commands.ReadRepositoryFile.OpenBlobAsync(repo, baseline, cancellation).ConfigureAwait(false);
            await using var remoteStream = await Commands.ReadRepositoryFile.OpenBlobAsync(repo, remote, cancellation).ConfigureAwait(false);
            var localStream = await Commands.ReadRepositoryFile.OpenBlobAsync(repo, local, cancellation).ConfigureAwait(false);
            try
            {
                var plan = SpreadsheetMergePlan.Create(path, SpreadsheetReader.Read(baseStream, cancellation),
                    SpreadsheetReader.Read(localStream, cancellation), SpreadsheetReader.Read(remoteStream, cancellation));
                return new PendingFile(plan, fullPath, workingHash, localStream);
            }
            catch
            {
                await localStream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static async Task<string> HashWorkingFileAsync(string file, CancellationToken cancellation)
        {
            if (!File.Exists(file))
                return null;
            await using var stream = File.OpenRead(file);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false));
        }

        private static async Task InstallFileAsync(PendingFile file, CancellationToken cancellation)
        {
            var temp = Path.Combine(Path.GetDirectoryName(file.FullPath)!, $".sourcegit-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await file.Output.CopyToAsync(output, cancellation);
                File.Move(temp, file.FullPath, overwrite: true);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        private static async Task<bool> StageFilesAsync(Repository repo, List<PendingFile> files, CommandLog log)
        {
            var paths = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(paths, string.Join('\0', files.Select(static file => file.Plan.Path)) + '\0');
                return await RunGitAsync(repo, $"--literal-pathspecs add --pathspec-from-file={paths.Quoted()} --pathspec-file-nul", log);
            }
            finally
            {
                File.Delete(paths);
            }
        }

        private static Task<bool> RunGitAsync(Repository repo, string args, CommandLog log) =>
            new Commands.Command { Context = repo.FullPath, WorkingDirectory = repo.FullPath,
                Editor = Commands.Command.EditorType.None, Args = args }.Use(log).ExecAsync();

        private sealed record PendingFile(SpreadsheetMergePlan Plan, string FullPath, string WorkingHash, Stream Template)
        {
            public MemoryStream Output { get; set; }
        }
    }
}
