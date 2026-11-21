using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace SourceGit.Models
{
    internal static class SpreadsheetReader
    {
        public static List<SpreadsheetSheet> Read(Stream source, CancellationToken cancellation)
        {
            using var package = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var workbookPart = ReadRelationships(package, "_rels/.rels", string.Empty).Values
                .FirstOrDefault(static item => item.Type.EndsWith("/officeDocument", StringComparison.Ordinal)).Path;
            if (string.IsNullOrEmpty(workbookPart))
                throw new InvalidDataException(App.Text("SpreadsheetError.NoWorkbook"));

            var separator = workbookPart.LastIndexOf('/');
            var workbookRels = $"{workbookPart[..(separator + 1)]}_rels/{workbookPart[(separator + 1)..]}.rels";
            var parts = ReadRelationships(package, workbookRels, workbookPart);
            var stringsPart = parts.Values.FirstOrDefault(static item => item.Type.EndsWith("/sharedStrings", StringComparison.Ordinal)).Path;
            var strings = stringsPart == null ? [] : ReadSharedStrings(package, stringsPart, cancellation);
            var workbook = ReadPart(package, workbookPart);
            var ns = workbook.Name.Namespace;
            var sheets = new List<SpreadsheetSheet>();
            var cellCount = 0;
            foreach (var sheet in workbook.Element(ns + "sheets")?.Elements(ns + "sheet") ?? [])
            {
                cancellation.ThrowIfCancellationRequested();
                var name = (string)sheet.Attribute("name");
                var id = sheet.Attributes().FirstOrDefault(static attr => attr.Name.LocalName == "id")?.Value;
                if (string.IsNullOrEmpty(name) || id == null || !parts.TryGetValue(id, out var part))
                    throw new InvalidDataException(App.Text("SpreadsheetError.MissingWorksheetRef"));

                if (part.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                    sheets.Add(ReadSheet(package, part.Path, name, strings, ref cellCount, cancellation));
            }

            if (sheets.Count == 0)
                throw new InvalidDataException(App.Text("SpreadsheetError.NoWorksheets"));

            return sheets;
        }

        internal static Dictionary<string, (string Type, string Path)> ReadRelationships(ZipArchive package, string path, string source)
        {
            var root = ReadPart(package, path);
            var ns = root.Name.Namespace;
            var origin = new Uri(PackageRoot, source);
            var result = new Dictionary<string, (string Type, string Path)>(StringComparer.Ordinal);
            foreach (var item in root.Elements(ns + "Relationship"))
            {
                var id = (string)item.Attribute("Id");
                var type = (string)item.Attribute("Type");
                var target = (string)item.Attribute("Target");
                if (id == null || type == null || string.IsNullOrEmpty(target) || (string)item.Attribute("TargetMode") == "External")
                    continue;

                if (Uri.TryCreate(origin, target, out var resolved) && resolved.Host == PackageRoot.Host &&
                    resolved.Scheme == PackageRoot.Scheme && resolved.Query.Length == 0 && resolved.Fragment.Length == 0)
                    result.Add(id, (type, Uri.UnescapeDataString(resolved.AbsolutePath).TrimStart('/')));
            }

            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive package, string part, CancellationToken cancellation)
        {
            using var reader = OpenPart(package, part);
            reader.MoveToContent();
            var ns = reader.NamespaceURI;
            var strings = new List<string>();
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si" || reader.NamespaceURI != ns)
                    continue;

                cancellation.ThrowIfCancellationRequested();
                using var item = reader.ReadSubtree();
                strings.Add(ReadText(XElement.Load(item)));
                if (strings.Count > MaxCells)
                    throw new InvalidDataException(App.Text("SpreadsheetError.TooManySharedStrings"));
            }

            return strings;
        }

        private static SpreadsheetSheet ReadSheet(ZipArchive package, string part, string name, List<string> strings,
            ref int cellCount, CancellationToken cancellation)
        {
            using var reader = OpenPart(package, part);
            reader.MoveToContent();
            var ns = reader.NamespaceURI;
            var rows = new List<SpreadsheetRow>();
            var columns = new SortedSet<int>();
            if (!reader.ReadToDescendant("sheetData", ns) || reader.IsEmptyElement)
                return new SpreadsheetSheet(name, rows, []);

            var dataDepth = reader.Depth;
            var rowNumber = 0;
            while (reader.Read() && reader.Depth > dataDepth)
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != ns)
                    continue;

                using var rowReader = reader.ReadSubtree();
                var row = XElement.Load(rowReader);
                rowNumber = (int?)row.Attribute("r") ?? rowNumber + 1;
                if (rowNumber is < 1 or > 1048576)
                    throw new InvalidDataException(App.Text("SpreadsheetError.InvalidRow"));

                var cells = new Dictionary<int, SpreadsheetCell>();
                var elementNs = row.Name.Namespace;
                var column = 0;
                foreach (var element in row.Elements(elementNs + "c"))
                {
                    var address = (string)element.Attribute("r");
                    column = address == null ? column + 1 : ReadColumn(address);
                    if (column is < 1 or > 16384)
                        throw new InvalidDataException(App.Text("SpreadsheetError.InvalidColumn"));

                    var cell = ReadCell(element, strings);
                    if (cell == null)
                        continue;

                    var formulaKind = (string)element.Element(elementNs + "f")?.Attribute("t");
                    cells.Add(column, formulaKind is null or "normal" ? cell : cell with { FormulaKind = formulaKind });
                    columns.Add(column);
                    if (++cellCount > MaxCells)
                        throw new InvalidDataException(App.Text("SpreadsheetError.TooManyCells"));
                }

                if (cells.Count > 0)
                    rows.Add(new SpreadsheetRow(rowNumber, cells));
            }

            rows.Sort(static (left, right) => left.Number.CompareTo(right.Number));
            return new SpreadsheetSheet(name, rows, [.. columns]);
        }

        private static SpreadsheetCell ReadCell(XElement cell, List<string> strings)
        {
            var ns = cell.Name.Namespace;
            var type = (string)cell.Attribute("t");
            var value = (string)cell.Element(ns + "v");
            var formula = (string)cell.Element(ns + "f");
            if (type == "inlineStr")
            {
                var inline = cell.Element(ns + "is");
                if (inline != null)
                    return new SpreadsheetCell("text", ReadText(inline), formula);
                return formula == null ? null : new SpreadsheetCell("text", null, formula);
            }

            if (value == null && formula == null)
                return null;

            // A string formula can legitimately cache an empty string.
            if (formula != null && value is { Length: 0 } && type != "str")
                value = null;

            switch (type)
            {
                case "s":
                    if (int.TryParse(value, out var index) && index >= 0 && index < strings.Count)
                        return new SpreadsheetCell("text", strings[index], formula);
                    if (value == null && formula != null)
                        return new SpreadsheetCell("text", null, formula);
                    throw new InvalidDataException(App.Text("SpreadsheetError.MissingSharedString"));
                case "str":
                    return new SpreadsheetCell("text", value, formula);
                case "b":
                    return new SpreadsheetCell("boolean", value == null ? null : value == "1" ? "TRUE" : "FALSE", formula);
                case "e":
                    return new SpreadsheetCell("error", value, formula);
                case "d":
                    return new SpreadsheetCell("date", value, formula);
                default:
                    return new SpreadsheetCell("number", NormalizeNumber(value), formula);
            }
        }

        private static string NormalizeNumber(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                return value;

            return number == 0 ? "0" : number.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ReadText(XElement element)
        {
            var ns = element.Name.Namespace;
            return string.Concat(element.Elements().Select(child =>
                child.Name == ns + "t" ? child.Value :
                child.Name == ns + "r" ? (string)child.Element(ns + "t") : null));
        }

        private static int ReadColumn(string address)
        {
            var column = 0;
            foreach (var part in address)
            {
                if (part is < 'A' or > 'Z')
                    break;
                column = column * 26 + part - 'A' + 1;
                if (column > 16384)
                    break;
            }

            return column;
        }

        internal static XElement ReadPart(ZipArchive package, string path)
        {
            using var reader = OpenPart(package, path);
            return XElement.Load(reader);
        }

        private static XmlReader OpenPart(ZipArchive package, string path)
        {
            var entry = package.GetEntry(path) ?? throw new InvalidDataException(App.Text("SpreadsheetError.MissingPart", path));
            if (entry.Length > MaxPartSize)
                throw new InvalidDataException(App.Text("SpreadsheetError.PartTooLarge"));

            return XmlReader.Create(entry.Open(), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CloseInput = true,
                MaxCharactersInDocument = MaxPartSize,
            });
        }

        private const int MaxCells = 1000000;
        private const int MaxPartSize = 128 * 1024 * 1024;
        private static readonly Uri PackageRoot = new("https://workbook.invalid/");
    }
}
