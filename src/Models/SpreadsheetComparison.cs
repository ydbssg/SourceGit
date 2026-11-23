using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SourceGit.Models
{
    public enum SpreadsheetChangeKind
    {
        None,
        Added,
        Deleted,
        Modified,
        FormulaChanged,
    }

    public sealed class SpreadsheetSheetVersion(SpreadsheetSheet sheet, bool isBefore, int idColumn)
    {
        public SpreadsheetSheet Sheet { get; } = sheet;
        public bool IsBefore { get; } = isBefore;
        public int IdColumn { get; } = idColumn;
        public IReadOnlyList<SpreadsheetRow> ChangedRows { get; private set; } = [];
        public IReadOnlyList<(SpreadsheetRow Row, int Column)> ChangedCells { get; private set; } = [];

        public SpreadsheetChangeKind GetChange(int row, int column) => _changes.GetValueOrDefault((row, column));

        internal void MarkChange(int row, int column, SpreadsheetChangeKind kind)
        {
            _changes[(row, column)] = kind;
            _changedRows.Add(row);
        }

        internal void PrepareDisplay()
        {
            if (_changedRows.Count == 0)
                return;

            var headerRowCount = IdColumn > 0 ? 4 : 1;
            ChangedRows = Sheet.Rows.FindAll(row => row.Number <= headerRowCount || _changedRows.Contains(row.Number));
            var rowsByNumber = ChangedRows.ToDictionary(static row => row.Number);
            ChangedCells = _changes.Keys
                .OrderBy(static cell => cell.Row)
                .ThenBy(cell => cell.Column == IdColumn ? 0 : cell.Column)
                .Select(cell => (rowsByNumber[cell.Row], cell.Column))
                .ToArray();
            _changedRows.Clear();
        }

        private readonly Dictionary<(int Row, int Column), SpreadsheetChangeKind> _changes = [];
        private readonly HashSet<int> _changedRows = [];
    }

    public sealed record SpreadsheetSheetComparison(string Name, SpreadsheetSheetVersion Before, SpreadsheetSheetVersion After,
        bool ByKey, int ChangeCount)
    {
        public string Mode => App.Text(ByKey ? "Diff.Spreadsheet.ByKey" : "Diff.Spreadsheet.ByAddress");
        public bool HasChanges => ChangeCount > 0;
    }

    internal static class SpreadsheetComparison
    {
        public static List<SpreadsheetSheetComparison> Create(List<SpreadsheetSheet> before, List<SpreadsheetSheet> after,
            bool configurationTable, CancellationToken cancellation)
        {
            var oldSheets = before.ToDictionary(static sheet => sheet.Name, StringComparer.Ordinal);
            var newSheets = after.ToDictionary(static sheet => sheet.Name, StringComparer.Ordinal);
            var results = new List<SpreadsheetSheetComparison>();
            foreach (var name in newSheets.Keys.Union(oldSheets.Keys, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                var oldSheet = oldSheets.GetValueOrDefault(name);
                var newSheet = newSheets.GetValueOrDefault(name);
                var oldLayout = configurationTable ? ReadLayout(oldSheet) : null;
                var newLayout = configurationTable ? ReadLayout(newSheet) : null;
                var idField = (oldLayout ?? newLayout)?.FirstField;
                var useKeys = idField != null &&
                    (oldSheet == null || oldLayout?.TryIndexRows(oldSheet, idField) == true) &&
                    (newSheet == null || newLayout?.TryIndexRows(newSheet, idField) == true);

                var oldRows = useKeys && oldLayout != null ? oldLayout.Rows : IndexByAddress(oldSheet);
                var newRows = useKeys && newLayout != null ? newLayout.Rows : IndexByAddress(newSheet);
                var oldFields = GetFields(oldSheet, useKeys ? oldLayout : null);
                var newFields = GetFields(newSheet, useKeys ? newLayout : null);
                var oldColumns = oldFields.ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);
                var newColumns = newFields.ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);
                var beforeVersion = oldSheet == null ? null : new SpreadsheetSheetVersion(oldSheet, true, useKeys ? oldLayout.IdColumn : 0);
                var afterVersion = newSheet == null ? null : new SpreadsheetSheetVersion(newSheet, false, useKeys ? newLayout.IdColumn : 0);
                var changeCount = 0;

                foreach (var key in newRows.Keys.Union(oldRows.Keys))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var oldRow = oldRows.GetValueOrDefault(key);
                    var newRow = newRows.GetValueOrDefault(key);
                    var oldCellFields = GetRowFields(oldRow, oldFields);
                    var newCellFields = GetRowFields(newRow, newFields);
                    foreach (var field in newCellFields.Union(oldCellFields, StringComparer.Ordinal))
                    {
                        var oldColumn = oldColumns.GetValueOrDefault(field);
                        var newColumn = newColumns.GetValueOrDefault(field);
                        var kind = GetChange(oldRow?.GetCell(oldColumn), newRow?.GetCell(newColumn));
                        if (kind == SpreadsheetChangeKind.None)
                            continue;

                        changeCount++;
                        if (oldRow != null && oldColumn != 0)
                            beforeVersion.MarkChange(oldRow.Number, oldColumn, kind);
                        if (newRow != null && newColumn != 0)
                            afterVersion.MarkChange(newRow.Number, newColumn, kind);
                    }
                }

                beforeVersion?.PrepareDisplay();
                afterVersion?.PrepareDisplay();
                results.Add(new SpreadsheetSheetComparison(name, beforeVersion, afterVersion, useKeys, changeCount));
            }

            return results;
        }

        private static SpreadsheetChangeKind GetChange(SpreadsheetCell before, SpreadsheetCell after)
        {
            if (before == after)
                return SpreadsheetChangeKind.None;
            if (before == null)
                return SpreadsheetChangeKind.Added;
            if (after == null)
                return SpreadsheetChangeKind.Deleted;
            if (before.Formula != after.Formula)
                return SpreadsheetChangeKind.FormulaChanged;

            // A save can remove a cached result without changing the formula.
            if (before.Formula != null && (before.Value == null || after.Value == null))
                return SpreadsheetChangeKind.None;

            return SpreadsheetChangeKind.Modified;
        }

        private static Dictionary<RowKey, SpreadsheetRow> IndexByAddress(SpreadsheetSheet sheet)
        {
            return sheet?.Rows.ToDictionary(static row => new RowKey(false, row.Number.ToString())) ?? [];
        }

        private static Dictionary<int, string> GetFields(SpreadsheetSheet sheet, TableLayout layout)
        {
            return layout?.FieldNames ?? sheet?.Columns.ToDictionary(static column => column, SpreadsheetSheet.ColumnName) ?? [];
        }

        private static IEnumerable<string> GetRowFields(SpreadsheetRow row, Dictionary<int, string> fields)
        {
            return row == null ? [] : row.Cells.Keys.Select(column => fields[column]);
        }

        private static TableLayout ReadLayout(SpreadsheetSheet sheet)
        {
            var header = sheet?.Rows.Find(static row => row.Number == 3);
            if (header == null || sheet.Columns.Count == 0)
                return null;

            var names = new Dictionary<int, string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in sheet.Columns)
            {
                var name = header.GetCell(column)?.Value?.Trim();
                if (string.IsNullOrEmpty(name) || !keys.Add(name))
                    return null;
                names.Add(column, name);
            }

            return new TableLayout(names[sheet.Columns[0]], names);
        }

        private readonly record struct RowKey(bool IsHeader, string Value);

        private sealed class TableLayout(string firstField, Dictionary<int, string> fieldNames)
        {
            public string FirstField { get; } = firstField;
            public Dictionary<int, string> FieldNames { get; } = fieldNames;
            public Dictionary<RowKey, SpreadsheetRow> Rows { get; } = [];
            public int IdColumn { get; private set; }

            public bool TryIndexRows(SpreadsheetSheet sheet, string idField)
            {
                IdColumn = FieldNames.FirstOrDefault(pair => pair.Value == idField).Key;
                if (IdColumn == 0)
                    return false;

                foreach (var row in sheet.Rows)
                {
                    var header = row.Number <= 4;
                    var id = header ? row.Number.ToString() : row.GetCell(IdColumn)?.Value;
                    if (string.IsNullOrWhiteSpace(id) || !Rows.TryAdd(new RowKey(header, id), row))
                        return false;
                }

                return true;
            }
        }

    }
}
