using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public sealed record SpreadsheetCell(string Kind, string Value, string Formula)
    {
        public string FormulaKind { get; init; }

        public string Display => Value ?? (string.IsNullOrEmpty(Formula) ? App.Text("Diff.Spreadsheet.NoFormulaResult") : $"={Formula}");

        public string ToolTipText
        {
            get
            {
                if (Formula == null)
                    return Display;

                if (Value == null)
                {
                    var notice = App.Text("Diff.Spreadsheet.NoFormulaResult");
                    return Formula.Length == 0 ? notice : $"{Display}\n{notice}";
                }

                return Formula.Length == 0 ? App.Text("Diff.Spreadsheet.FormulaValue", Display) : $"{Display}\n={Formula}";
            }
        }
    }

    public sealed record SpreadsheetRow(int Number, Dictionary<int, SpreadsheetCell> Cells)
    {
        public SpreadsheetCell GetCell(int column) => Cells.GetValueOrDefault(column);
    }

    public sealed record SpreadsheetSheet(string Name, List<SpreadsheetRow> Rows, List<int> Columns)
    {
        public static string ColumnName(int column)
        {
            Span<char> name = stackalloc char[3];
            var start = name.Length;
            while (column > 0)
            {
                column--;
                name[--start] = (char)('A' + column % 26);
                column /= 26;
            }

            return new string(name[start..]);
        }
    }
}
