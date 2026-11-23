using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public sealed class ConfigTableDiff(string repo, Models.DiffOption option, Models.BinaryDiff summary) : ObservableObject
    {
        internal string Repository => repo;
        internal Models.DiffFile? RawFile => NewSize > 0 ? option.NewFile : option.OldFile;
        public long OldSize => summary.OldSize;
        public long NewSize => summary.NewSize;
        public bool CanOpenHex => NewSize > 0 || OldSize > 0;
        public bool HasLoaded => Comparison != null;
        public bool HasChanges => Comparison?.Rows.Count > 0;

        public Models.ConfigTableChanges Comparison
        {
            get => _comparison;
            private set
            {
                if (SetProperty(ref _comparison, value))
                {
                    OnPropertyChanged(nameof(HasLoaded));
                    OnPropertyChanged(nameof(HasChanges));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string Error
        {
            get => _error;
            private set => SetProperty(ref _error, value);
        }

        public async Task LoadAsync()
        {
            if (IsLoading || HasLoaded)
                return;

            using var cancellation = new CancellationTokenSource();
            _loading = cancellation;
            IsLoading = true;
            Error = string.Empty;
            try
            {
                var token = cancellation.Token;
                var comparison = await Task.Run(async () =>
                {
                    var before = await ReadVersionAsync(option.OldFile, App.Text("Diff.Old"), token).ConfigureAwait(false);
                    var after = await ReadVersionAsync(option.NewFile, App.Text("Diff.New"), token).ConfigureAwait(false);
                    return Models.ConfigTableComparison.Compare(before, after, token);
                }, token);

                token.ThrowIfCancellationRequested();
                Comparison = comparison;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The user left this comparison.
            }
            catch (Exception e)
            {
                if (!cancellation.IsCancellationRequested)
                    Error = App.Text("Diff.ConfigTable.LoadFailed", e.Message);
            }
            finally
            {
                _loading = null;
                IsLoading = false;
            }
        }

        public void CancelLoad() => _loading?.Cancel();

        private async Task<Models.ConfigTable> ReadVersionAsync(Models.DiffFile? version, string label, CancellationToken cancellation)
        {
            if (version is not { } file)
                return null;

            try
            {
                var schemaPath = Models.ConfigTableReader.GetSchemaPath(file.Path)
                    ?? Models.ConfigTableReader.GetSchemaPath(option.Path);
                var schemaFile = new Models.DiffFile(file.Revision, schemaPath);
                await using var schema = await Commands.ReadRepositoryFile.OpenAsync(repo, schemaFile, cancellation).ConfigureAwait(false);
                using var reader = new StreamReader(schema);
                var source = await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);

                await using var stream = await Commands.ReadRepositoryFile.OpenAsync(repo, file, cancellation).ConfigureAwait(false);
                var data = new byte[stream.Length];
                await stream.ReadExactlyAsync(data, cancellation).ConfigureAwait(false);
                var table = Path.GetFileNameWithoutExtension(file.Path);
                return Models.ConfigTableReader.Read(data, source, table, cancellation);
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException($"{label} · {e.Message}", e);
            }
        }

        private Models.ConfigTableChanges _comparison = null;
        private bool _isLoading;
        private string _error = string.Empty;
        private CancellationTokenSource _loading = null;
    }
}
