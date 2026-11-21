using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    internal static class ReadSpreadsheetFile
    {
        public static async Task<Stream> OpenAsync(string repo, Models.DiffFile file, CancellationToken cancellation)
        {
            var stream = file.Revision == null
                ? OpenLocalFile(Path.Combine(repo, file.Path))
                : await ReadGitOutputAsync(repo, ["cat-file", "blob", $"{file.Revision}:{file.Path}"], null, cancellation).ConfigureAwait(false);

            return await HydrateAsync(repo, stream, cancellation).ConfigureAwait(false);
        }

        internal static async Task<Stream> OpenBlobAsync(string repo, string oid, CancellationToken cancellation)
        {
            var stream = await ReadGitOutputAsync(repo, ["cat-file", "blob", oid], null, cancellation).ConfigureAwait(false);
            return await HydrateAsync(repo, stream, cancellation).ConfigureAwait(false);
        }

        internal static async Task<string> ReadUnmergedAsync(string repo, CancellationToken cancellation)
        {
            await using var stream = await ReadGitOutputAsync(repo, ["ls-files", "-u", "-z"], null, cancellation).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);
        }

        private static async Task<Stream> HydrateAsync(string repo, Stream stream, CancellationToken cancellation)
        {
            try
            {
                if (stream.Length is > 0 and <= 1024)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
                    var text = await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);
                    var pointer = Models.LFSObject.Parse(text);
                    if (pointer is { Oid.Length: 64 })
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                        return await OpenLFSAsync(repo, pointer, text, cancellation).ConfigureAwait(false);
                    }

                    stream.Position = 0;
                }

                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static async Task<Stream> OpenLFSAsync(string repo, Models.LFSObject pointer, string text, CancellationToken cancellation)
        {
            if (pointer.Size > MaxFileBytes)
                throw new InvalidDataException(App.Text("SpreadsheetError.FileTooLarge"));

            var dir = await new QueryGitCommonDir(repo).GetResultAsync().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(dir))
            {
                var cache = Path.Combine(dir, "lfs", "objects", pointer.Oid[..2], pointer.Oid[2..4], pointer.Oid);
                if (File.Exists(cache))
                    return OpenLocalFile(cache);
            }

            var stream = await ReadGitOutputAsync(repo, ["lfs", "smudge"], text, cancellation).ConfigureAwait(false);
            if (stream.Length != pointer.Size)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException(App.Text("SpreadsheetError.IncompleteLFS"));
            }

            return stream;
        }

        private static FileStream OpenLocalFile(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length <= MaxFileBytes)
                return stream;

            stream.Dispose();
            throw new InvalidDataException(App.Text("SpreadsheetError.FileTooLarge"));
        }

        private static async Task<Stream> ReadGitOutputAsync(string repo, string[] args, string input, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo
            {
                WorkingDirectory = repo,
                FileName = Native.OS.GitExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = input != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start) ?? throw new IOException(App.Text("SpreadsheetError.GitStart"));
            var errors = process.StandardError.ReadToEndAsync(cancellation);
            var stream = new MemoryStream();
            try
            {
                if (input != null)
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), cancellation).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                var buffer = new byte[64 * 1024];
                var output = process.StandardOutput.BaseStream;
                int count;
                while ((count = await output.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    if (stream.Length + count > MaxFileBytes)
                        throw new InvalidDataException(App.Text("SpreadsheetError.FileTooLarge"));
                    stream.Write(buffer, 0, count);
                }

                var message = (await errors.ConfigureAwait(false)).Trim();
                await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new IOException(string.IsNullOrEmpty(message)
                        ? App.Text("SpreadsheetError.GitRead")
                        : Models.GitOutputText.TranslateMessage(message));

                stream.Position = 0;
                return stream;
            }
            catch
            {
                stream.Dispose();
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The Git process already exited.
                }

                throw;
            }
        }

        private const long MaxFileBytes = 128L * 1024 * 1024;
    }
}
