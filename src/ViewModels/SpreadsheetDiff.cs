using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public sealed class SpreadsheetDiff(string repo, Models.DiffOption option) : ObservableObject
    {
        public List<Models.SpreadsheetSheetComparison> Sheets
        {
            get => _sheets;
            private set
            {
                if (SetProperty(ref _sheets, value))
                    OnPropertyChanged(nameof(HasSheets));
            }
        }

        public Models.SpreadsheetSheetComparison SelectedSheet
        {
            get => _selectedSheet;
            set
            {
                if (SetProperty(ref _selectedSheet, value))
                    NotifyVisibleSheetChanged();
            }
        }

        public bool HasSheets => Sheets.Count > 0;
        public Models.SpreadsheetSheetVersion VisibleVersion => _showBefore ? SelectedSheet?.Before : SelectedSheet?.After;
        public string VisibleVersionLabel => App.Text(_showBefore ? "Diff.Old" : "Diff.New");
        public string SwitchVersionLabel => App.Text(_showBefore ? "Diff.Spreadsheet.ViewAfter" : "Diff.Spreadsheet.ViewBefore");

        public bool OnlyChangedRows
        {
            get => _onlyChangedRows;
            set
            {
                if (SetProperty(ref _onlyChangedRows, value))
                {
                    NotifyVisibleRowsChanged();
                    ResetChangeNavigation();
                }
            }
        }

        public bool HasVisibleRows => VisibleVersion != null &&
            (OnlyChangedRows ? VisibleVersion.ChangedRows.Count > 0 : VisibleVersion.Sheet.Rows.Count > 0);
        public bool CanLocateChange => VisibleVersion?.ChangedCells.Count > 0;
        public bool CanLocatePreviousChange => CanLocateChange && _locatedChangeIndex > 0;
        public bool CanLocateNextChange => CanLocateChange && _locatedChangeIndex < VisibleVersion.ChangedCells.Count - 1;

        public string LocatedChange
        {
            get
            {
                if (!CanLocateChange || _locatedChangeIndex < 0)
                    return string.Empty;

                var (row, column) = VisibleVersion.ChangedCells[_locatedChangeIndex];
                return $"{Models.SpreadsheetSheet.ColumnName(column)}{row.Number} · {_locatedChangeIndex + 1} / {VisibleVersion.ChangedCells.Count}";
            }
        }

        public string EmptyMessage
        {
            get
            {
                if (SelectedSheet == null || HasVisibleRows)
                    return string.Empty;
                if (VisibleVersion == null)
                    return App.Text("Diff.Spreadsheet.NoSheet");
                if (!OnlyChangedRows)
                    return App.Text("Diff.Spreadsheet.EmptySheet");
                return App.Text(SelectedSheet.HasChanges ? "Diff.Spreadsheet.NoChangesInVersion" : "Diff.Spreadsheet.NoChanges");
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
            if (IsLoading || HasSheets)
                return;

            using var cancellation = new CancellationTokenSource();
            _loading = cancellation;
            IsLoading = true;
            Error = string.Empty;
            try
            {
                var sheets = await Task.Run(async () =>
                {
                    var token = cancellation.Token;
                    var before = await ReadVersionAsync(option.OldFile, token).ConfigureAwait(false);
                    var after = await ReadVersionAsync(option.NewFile, token).ConfigureAwait(false);
                    if (before.Count == 0 && after.Count == 0)
                        throw new InvalidDataException(App.Text("SpreadsheetError.NoVersions"));

                    var configurationTable = option.Path.Replace('\\', '/')
                        .StartsWith("minifire_client/Configs/tables/", StringComparison.OrdinalIgnoreCase);
                    return Models.SpreadsheetComparison.Create(before, after, configurationTable, token);
                }, cancellation.Token);

                cancellation.Token.ThrowIfCancellationRequested();
                Sheets = sheets;
                SelectedSheet = sheets.Find(static sheet => sheet.HasChanges) ?? sheets[0];
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The user left this comparison.
            }
            catch (Exception e)
            {
                if (!cancellation.IsCancellationRequested)
                    Error = App.Text("Diff.Spreadsheet.LoadFailed", e.Message);
            }
            finally
            {
                _loading = null;
                IsLoading = false;
            }
        }

        public void CancelLoad() => _loading?.Cancel();

        public void SwitchVersion()
        {
            _showBefore = !_showBefore;
            OnPropertyChanged(nameof(VisibleVersionLabel));
            OnPropertyChanged(nameof(SwitchVersionLabel));
            NotifyVisibleSheetChanged();
        }

        public (Models.SpreadsheetRow Row, int Column)? LocateChange(int offset)
        {
            if (!CanLocateChange)
                return null;

            var cells = VisibleVersion.ChangedCells;
            _locatedChangeIndex = _locatedChangeIndex < 0 ? 0 : Math.Clamp(_locatedChangeIndex + offset, 0, cells.Count - 1);
            NotifyChangeNavigation();
            return cells[_locatedChangeIndex];
        }

        private void NotifyVisibleSheetChanged()
        {
            OnPropertyChanged(nameof(VisibleVersion));
            NotifyVisibleRowsChanged();
            ResetChangeNavigation();
        }

        private void NotifyVisibleRowsChanged()
        {
            OnPropertyChanged(nameof(HasVisibleRows));
            OnPropertyChanged(nameof(EmptyMessage));
        }

        private void ResetChangeNavigation()
        {
            _locatedChangeIndex = -1;
            NotifyChangeNavigation();
        }

        private void NotifyChangeNavigation()
        {
            OnPropertyChanged(nameof(CanLocateChange));
            OnPropertyChanged(nameof(CanLocatePreviousChange));
            OnPropertyChanged(nameof(CanLocateNextChange));
            OnPropertyChanged(nameof(LocatedChange));
        }

        private async Task<List<Models.SpreadsheetSheet>> ReadVersionAsync(Models.DiffFile? file, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (file == null)
                return [];

            await using var stream = await Commands.ReadSpreadsheetFile.OpenAsync(repo, file.Value, cancellation).ConfigureAwait(false);
            return Models.SpreadsheetReader.Read(stream, cancellation);
        }

        private List<Models.SpreadsheetSheetComparison> _sheets = [];
        private Models.SpreadsheetSheetComparison _selectedSheet = null;
        private bool _showBefore;
        private bool _onlyChangedRows = true;
        private int _locatedChangeIndex = -1;
        private bool _isLoading = false;
        private string _error = string.Empty;
        private CancellationTokenSource _loading = null;
    }
}
