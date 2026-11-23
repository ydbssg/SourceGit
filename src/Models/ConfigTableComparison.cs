using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SourceGit.Models
{
    public readonly record struct ConfigCellDiff(string Before, string After)
    {
        public bool IsChanged => Before != After;
    }

    public sealed record ConfigRowDiff(ConfigCellDiff[] Cells, bool IsSchema = false);

    public sealed record ConfigTableChanges(string[] Fields, IReadOnlyList<ConfigRowDiff> Rows);

    internal static class ConfigTableComparison
    {
        public static ConfigTableChanges Compare(ConfigTable before, ConfigTable after, CancellationToken cancellation)
        {
            var oldColumns = before?.Columns ?? [];
            var newColumns = after?.Columns ?? [];
            var fields = AlignFields(oldColumns, newColumns);
            var rows = new List<ConfigRowDiff>();
            if (before != null && after != null)
            {
                var schema = CompareSchema(fields, oldColumns, newColumns);
                if (schema != null)
                    rows.Add(schema);
            }

            var oldRows = before?.Rows ?? [];
            var newRows = after?.Rows ?? [];
            foreach (var key in oldRows.Keys.Union(newRows.Keys, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                var oldRow = oldRows.GetValueOrDefault(key);
                var newRow = newRows.GetValueOrDefault(key);
                if (!HasValueChanges(oldRow, newRow, fields))
                    continue;

                var cells = new ConfigCellDiff[fields.Count];
                for (var i = 0; i < fields.Count; i++)
                    cells[i] = fields[i].GetCell(oldRow, newRow);
                rows.Add(new ConfigRowDiff(cells));
            }

            return new ConfigTableChanges(fields.Select(static field => field.Name).ToArray(), rows);
        }

        private static bool HasValueChanges(string[] before, string[] after, List<ColumnPair> fields)
        {
            if (before == null || after == null)
                return true;

            foreach (var field in fields)
            {
                if (field.GetCell(before, after).IsChanged)
                    return true;
            }

            return false;
        }

        private static List<ColumnPair> AlignFields(ConfigColumn[] before, ConfigColumn[] after)
        {
            var fields = new List<ColumnPair>();
            var oldKey = before.FirstOrDefault()?.Name;
            var newKey = after.FirstOrDefault()?.Name;
            if (oldKey == null && newKey == null)
                return fields;

            var keyName = oldKey != null && newKey != null && oldKey != newKey ? $"{oldKey} → {newKey}" : newKey ?? oldKey;
            fields.Add(new ColumnPair(keyName, oldKey == null ? -1 : 0, newKey == null ? -1 : 0));

            var oldFields = IndexFields(before);
            var newFields = IndexFields(after);
            var oldNames = before.Skip(1).Select(static field => field.Name);
            var newNames = after.Skip(1).Select(static field => field.Name);
            foreach (var name in newNames.Union(oldNames, StringComparer.Ordinal))
                fields.Add(new ColumnPair(name, oldFields.GetValueOrDefault(name, -1), newFields.GetValueOrDefault(name, -1)));
            return fields;
        }

        private static ConfigRowDiff CompareSchema(List<ColumnPair> fields, ConfigColumn[] before, ConfigColumn[] after)
        {
            var cells = new ConfigCellDiff[fields.Count];
            var changed = false;
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var oldColumn = field.BeforeIndex < 0 ? null : before[field.BeforeIndex];
                var newColumn = field.AfterIndex < 0 ? null : after[field.AfterIndex];
                if (oldColumn == newColumn)
                    continue;

                cells[i] = new ConfigCellDiff(oldColumn?.TypeName, newColumn?.TypeName);
                if (oldColumn != null && newColumn != null && oldColumn.Name != newColumn.Name)
                    cells[i] = new ConfigCellDiff($"{oldColumn.Name} ({oldColumn.TypeName})", $"{newColumn.Name} ({newColumn.TypeName})");
                changed = true;
            }

            return changed ? new ConfigRowDiff(cells, IsSchema: true) : null;
        }

        private static Dictionary<string, int> IndexFields(ConfigColumn[] definitions)
        {
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 1; i < definitions.Length; i++)
                columns.Add(definitions[i].Name, i);
            return columns;
        }

        private readonly record struct ColumnPair(string Name, int BeforeIndex, int AfterIndex)
        {
            public ConfigCellDiff GetCell(string[] before, string[] after) => new(
                BeforeIndex < 0 ? null : before?[BeforeIndex],
                AfterIndex < 0 ? null : after?[AfterIndex]);
        }
    }
}
