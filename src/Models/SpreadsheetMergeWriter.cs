using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace SourceGit.Models
{
    internal static class SpreadsheetMergeWriter
    {
        public static MemoryStream Write(Stream template, SpreadsheetMergePlan plan, CancellationToken cancellation)
        {
            if (plan.Rows.Any(static row => !row.Resolved))
                throw new InvalidOperationException(App.Text("SpreadsheetError.UnresolvedRows"));

            var output = new MemoryStream();
            try
            {
                template.CopyTo(output);
                output.Position = 0;
                var expected = new Dictionary<(string Sheet, int Number), SpreadsheetMergeRow>();
                using (var book = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
                {
                    var workbookPath = SpreadsheetReader.ReadRelationships(book, "_rels/.rels", string.Empty).Values
                        .Single(static item => item.Type.EndsWith("/officeDocument", StringComparison.Ordinal)).Path;
                    var lastSlash = workbookPath.LastIndexOf('/') + 1;
                    var parts = SpreadsheetReader.ReadRelationships(book,
                        $"{workbookPath[..lastSlash]}_rels/{workbookPath[lastSlash..]}.rels", workbookPath);
                    var workbook = SpreadsheetReader.ReadPart(book, workbookPath);
                    var ns = workbook.Name.Namespace;
                    var changes = plan.Rows.Where(static row => !SpreadsheetMergePlan.RowsEqual(row.Local, row.Result));
                    foreach (var sheetEdits in changes.GroupBy(static row => row.Sheet))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var sheet = workbook.Element(ns + "sheets")!.Elements(ns + "sheet")
                            .Single(item => (string)item.Attribute("name") == sheetEdits.Key);
                        var reference = sheet.Attributes().Single(static attribute => attribute.Name.LocalName == "id").Value;
                        var path = parts[reference].Path;
                        var worksheet = SpreadsheetReader.ReadPart(book, path);
                        var sheetNs = worksheet.Name.Namespace;
                        var data = worksheet.Element(sheetNs + "sheetData")!;
                        var rows = data.Elements(sheetNs + "row").ToDictionary(static row => (int)row.Attribute("r")!);
                        foreach (var edit in sheetEdits)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var number = ApplyRow(data, rows, edit);
                            expected[(edit.Sheet, number)] = edit;
                        }

                        // Excel can recompute the optional used-range metadata on open.
                        worksheet.Element(sheetNs + "dimension")?.Remove();
                        Replace(book, path, worksheet);
                    }

                    if (expected.Count > 0)
                    {
                        var calc = workbook.Element(ns + "calcPr");
                        if (calc != null)
                        {
                            calc.SetAttributeValue("fullCalcOnLoad", "1");
                            Replace(book, workbookPath, workbook);
                        }
                    }
                }

                output.Position = 0;
                var readBack = SpreadsheetReader.Read(output, cancellation).ToDictionary(static sheet => sheet.Name);
                foreach (var (key, edit) in expected)
                {
                    var row = readBack[key.Sheet].Rows.Find(item => item.Number == key.Number);
                    if (!SpreadsheetMergePlan.RowsEqual(row, edit.Result))
                        throw new InvalidDataException(App.Text("SpreadsheetError.Verification", edit.Label));
                }

                output.Position = 0;
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        private static int ApplyRow(XElement data, Dictionary<int, XElement> rows, SpreadsheetMergeRow edit)
        {
            var ns = data.Name.Namespace;
            if (edit.Result == null)
            {
                if (edit.Local.Cells.Values.Any(static cell => HasGroupedFormula(cell)))
                    throw UnsupportedFormula(edit);
                var deleted = edit.Local.Number;
                rows[deleted].Remove();
                rows.Remove(deleted);
                return deleted;
            }

            var number = edit.Local?.Number ?? edit.Result.Number;
            if (edit.Local == null)
            {
                if (rows.ContainsKey(number))
                    number = rows.Keys.Max() + 1;
                if (number > 1048576)
                    throw new InvalidDataException(App.Text("SpreadsheetError.RowLimit", edit.Label));

                var after = rows.Keys.Where(key => key > number).DefaultIfEmpty(0).Min();
                var added = new XElement(ns + "row", new XAttribute("r", number));
                if (after == 0)
                    data.Add(added);
                else
                    rows[after].AddBeforeSelf(added);
                rows.Add(number, added);
            }

            var target = rows[number];
            var cells = target.Elements(ns + "c").ToDictionary(static item => ReadColumn((string)item.Attribute("r")!));
            IEnumerable<int> originalColumns = edit.Local?.Cells.Keys ?? Enumerable.Empty<int>();
            foreach (var column in originalColumns.Union(edit.Result.Cells.Keys).Order())
            {
                var previous = edit.Local?.GetCell(column);
                var value = edit.Result.GetCell(column);
                if (SpreadsheetMergePlan.CellsEqual(previous, value))
                    continue;

                if (HasGroupedFormula(previous) || HasGroupedFormula(value) ||
                    (value?.Formula != null && !edit.FormulaUsesOriginalPosition(column, value, number)))
                    throw UnsupportedFormula(edit);

                if (!cells.TryGetValue(column, out var cell))
                {
                    var address = $"{SpreadsheetSheet.ColumnName(column)}{number}";
                    cell = new XElement(ns + "c", new XAttribute("r", address));
                    var after = cells.Keys.Where(key => key > column).DefaultIfEmpty(0).Min();
                    if (after == 0)
                        target.Add(cell);
                    else
                        cells[after].AddBeforeSelf(cell);
                    cells.Add(column, cell);
                }

                SetCell(cell, value);
            }

            return number;
        }

        private static void SetCell(XElement element, SpreadsheetCell value)
        {
            var ns = element.Name.Namespace;
            element.Elements().Where(item => item.Name == ns + "f" || item.Name == ns + "v" || item.Name == ns + "is").Remove();
            element.SetAttributeValue("t", null);
            if (value == null)
                return;

            if (value.Formula != null)
                element.AddFirst(new XElement(ns + "f", value.Formula));
            if (value.Kind == "text" && value.Formula == null)
            {
                element.SetAttributeValue("t", "inlineStr");
                element.AddFirst(new XElement(ns + "is", new XElement(ns + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), value.Value)));
                return;
            }

            var type = value.Kind switch
            {
                "text" => "str",
                "boolean" => "b",
                "date" => "d",
                "error" => "e",
                _ => null,
            };
            element.SetAttributeValue("t", type);
            if (value.Value != null)
            {
                var content = value.Kind == "boolean" ? (value.Value == "TRUE" ? "1" : "0") : value.Value;
                var node = new XElement(ns + "v", content);
                if (value.Formula != null)
                    element.Element(ns + "f")!.AddAfterSelf(node);
                else
                    element.AddFirst(node);
            }
        }

        private static bool HasGroupedFormula(SpreadsheetCell cell) =>
            cell?.Formula != null && cell.FormulaKind is not (null or "normal");

        private static InvalidDataException UnsupportedFormula(SpreadsheetMergeRow row) =>
            new(App.Text("SpreadsheetError.UnsupportedFormula", row.Label));

        private static int ReadColumn(string address)
        {
            var column = 0;
            foreach (var character in address)
            {
                if (character is < 'A' or > 'Z')
                    break;
                column = column * 26 + character - 'A' + 1;
            }

            return column;
        }

        private static void Replace(ZipArchive package, string path, XElement xml)
        {
            package.GetEntry(path)!.Delete();
            using var output = package.CreateEntry(path, CompressionLevel.Optimal).Open();
            xml.Save(output, SaveOptions.DisableFormatting);
        }
    }
}
