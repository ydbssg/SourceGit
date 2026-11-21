using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SourceGit.Models
{
    internal sealed class SpreadsheetMergePlan(string path)
    {
        public string Path { get; } = path;
        public List<SpreadsheetMergeRow> Rows { get; } = [];

        public static bool Supports(string path) =>
            path.StartsWith("minifire_client/Configs/tables/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        public static SpreadsheetMergePlan Create(string path, List<SpreadsheetSheet> baseline,
            List<SpreadsheetSheet> local, List<SpreadsheetSheet> remote)
        {
            var plan = new SpreadsheetMergePlan(path);
            var localSheets = local.ToDictionary(static sheet => sheet.Name);
            var remoteSheets = remote.ToDictionary(static sheet => sheet.Name);
            if (!baseline.Select(static sheet => sheet.Name).ToHashSet().SetEquals(remoteSheets.Keys))
                throw new InvalidDataException(App.Text("SpreadsheetError.SheetsChanged"));

            foreach (var common in baseline)
            {
                var theirs = remoteSheets[common.Name];
                if (SheetEquals(common, theirs))
                    continue;

                if (!localSheets.TryGetValue(common.Name, out var mine))
                    throw new InvalidDataException(App.Text("SpreadsheetError.LocalSheetRemoved", common.Name));

                var header = common.Rows.Find(static row => row.Number == 3);
                if (header == null || common.Columns.Count == 0)
                    throw new InvalidDataException(App.Text("SpreadsheetError.HeaderMissing", common.Name));

                var fields = header.Cells.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value);
                var keyColumn = common.Columns[0];
                if (!fields.ContainsKey(keyColumn) || fields.Values.Any(string.IsNullOrWhiteSpace) ||
                    fields.Values.Distinct(StringComparer.Ordinal).Count() != fields.Count ||
                    !HeadersEqual(common, mine) || !HeadersEqual(common, theirs))
                    throw new InvalidDataException(App.Text("SpreadsheetError.SchemaChange", common.Name));

                var commonRows = IndexRows(common, keyColumn, fields);
                var myRows = IndexRows(mine, keyColumn, fields);
                var theirRows = IndexRows(theirs, keyColumn, fields);
                foreach (var id in myRows.Keys.Union(theirRows.Keys).Union(commonRows.Keys))
                {
                    var original = commonRows.GetValueOrDefault(id);
                    var myRow = myRows.GetValueOrDefault(id);
                    var theirRow = theirRows.GetValueOrDefault(id);
                    if (RowsEqual(original, theirRow))
                        continue;

                    var row = new SpreadsheetMergeRow(path, common.Name, id, original, myRow, theirRow, fields);
                    if (RowsEqual(original, myRow))
                        row.Resolve(SpreadsheetMergeSide.Remote);
                    plan.Rows.Add(row);
                }
            }

            return plan;
        }

        internal static bool CellsEqual(SpreadsheetCell first, SpreadsheetCell second)
        {
            if (first == second)
                return true;
            if (first == null || second == null || first.Formula != second.Formula)
                return false;
            return first.Formula != null
                ? first.FormulaKind == second.FormulaKind
                : first.Kind == second.Kind && first.Value == second.Value;
        }

        internal static bool RowsEqual(SpreadsheetRow first, SpreadsheetRow second)
        {
            if (first == second)
                return true;
            return first != null && second != null && first.Cells.Count == second.Cells.Count &&
                first.Cells.All(pair => CellsEqual(pair.Value, second.GetCell(pair.Key)));
        }

        private static bool SheetEquals(SpreadsheetSheet first, SpreadsheetSheet second) =>
            first.Rows.Count == second.Rows.Count && first.Rows.Zip(second.Rows)
                .All(static pair => pair.First.Number == pair.Second.Number && RowsEqual(pair.First, pair.Second));

        private static bool HeadersEqual(SpreadsheetSheet first, SpreadsheetSheet second)
        {
            for (var row = 1; row <= 4; row++)
            {
                if (!RowsEqual(first.Rows.Find(item => item.Number == row), second.Rows.Find(item => item.Number == row)))
                    return false;
            }

            return true;
        }

        private static Dictionary<string, SpreadsheetRow> IndexRows(SpreadsheetSheet sheet, int keyColumn, Dictionary<int, string> fields)
        {
            var rows = new Dictionary<string, SpreadsheetRow>(StringComparer.Ordinal);
            foreach (var row in sheet.Rows.SkipWhile(static row => row.Number <= 4))
            {
                var key = row.GetCell(keyColumn);
                if (key == null || string.IsNullOrWhiteSpace(key.Value) || key.Formula != null ||
                    row.Cells.Keys.Any(column => !fields.ContainsKey(column)) || !rows.TryAdd(key.Value, row))
                    throw new InvalidDataException(App.Text("SpreadsheetError.InvalidFields", sheet.Name));
            }

            return rows;
        }
    }

    public enum SpreadsheetMergeSide
    {
        Local,
        Remote,
        Base,
    }

    public sealed record SpreadsheetMergeChoice(SpreadsheetMergeSide Side, string Label);

    public sealed class SpreadsheetMergeField(int column, string name, SpreadsheetCell baseline,
        SpreadsheetCell local, SpreadsheetCell remote)
    {
        public int Column { get; } = column;
        public string Name { get; } = name;
        public string BaseValue => baseline?.Display ?? "—";
        public string LocalValue => local?.Display ?? "—";
        public string RemoteValue => remote?.Display ?? "—";
        public List<SpreadsheetMergeChoice> Choices { get; set; }
        public SpreadsheetMergeChoice SelectedChoice { get; set; }
        public SpreadsheetCell Value => SelectedChoice?.Side switch
        {
            SpreadsheetMergeSide.Remote => remote,
            SpreadsheetMergeSide.Base => baseline,
            _ => local,
        };

        public SpreadsheetMergeSide PreferredSide =>
            SpreadsheetMergePlan.CellsEqual(baseline, local) && !SpreadsheetMergePlan.CellsEqual(baseline, remote)
                ? SpreadsheetMergeSide.Remote : SpreadsheetMergeSide.Local;
    }

    public sealed class SpreadsheetMergeRow(string path, string sheet, string id, SpreadsheetRow baseline,
        SpreadsheetRow local, SpreadsheetRow remote, Dictionary<int, string> fields)
    {
        public string Label => $"{System.IO.Path.GetFileName(path)} / {Sheet} / ID {Id}";
        public string Sheet { get; } = sheet;
        public string Id { get; } = id;
        public SpreadsheetRow Local { get; } = local;
        public SpreadsheetRow Result { get; private set; }
        public bool Resolved { get; private set; }
        public bool CanCombineFields => Local != null && remote != null;
        public bool HasLocalRow => Local != null;
        public bool HasRemoteRow => remote != null;

        public List<SpreadsheetMergeField> Fields { get; } = fields.OrderBy(static field => field.Key)
            .Select(field => new SpreadsheetMergeField(field.Key, field.Value, baseline?.GetCell(field.Key),
                local?.GetCell(field.Key), remote?.GetCell(field.Key))).ToList();

        public void Resolve(SpreadsheetMergeSide side)
        {
            Result = side switch
            {
                SpreadsheetMergeSide.Remote => remote,
                SpreadsheetMergeSide.Base => baseline,
                _ => Local,
            };
            Resolved = true;
        }

        public void ResolveFields()
        {
            if (!CanCombineFields)
                return;

            Result = new SpreadsheetRow(Local.Number, Fields.Where(static field => field.Value != null)
                .ToDictionary(static field => field.Column, static field => field.Value));
            Resolved = true;
        }

        internal bool FormulaUsesOriginalPosition(int column, SpreadsheetCell cell, int number) =>
            (Local?.Number == number && ReferenceEquals(Local.GetCell(column), cell)) ||
            (remote?.Number == number && ReferenceEquals(remote.GetCell(column), cell)) ||
            (baseline?.Number == number && ReferenceEquals(baseline.GetCell(column), cell));
    }
}
