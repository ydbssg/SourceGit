using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace SourceGit.Models
{
    internal enum ConfigValueType { Int, Float, String }

    internal sealed record ConfigColumn(string Name, ConfigValueType Type, int ArrayDepth)
    {
        public string TypeName => Type.ToString().ToLowerInvariant() + (ArrayDepth switch
        {
            1 => "[]",
            2 => "[][]",
            _ => string.Empty,
        });
    }

    internal sealed record ConfigTable(ConfigColumn[] Columns, Dictionary<string, string[]> Rows);

    internal static class ConfigTableReader
    {
        public static string GetSchemaPath(string path)
        {
            if (path == null || !path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                return null;

            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.Equals(directory, "minifire_client/Assets/GameAssets/Configs/Config", StringComparison.OrdinalIgnoreCase))
                return "minifire_client/Assets/Scripts/Configs/Configs.cs";
            if (string.Equals(directory, "minifire_server/Assets/StreamingAssets/Configs", StringComparison.OrdinalIgnoreCase))
                return "minifire_server/Assets/Scripts/ServerConfigs/Configs.cs";
            return null;
        }

        public static ConfigTable Read(byte[] data, string source, string table, CancellationToken cancellation)
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("Incomplete MiniFire table header.");

            var magic = data.AsSpan(0, 4);
            var indexedText = magic.SequenceEqual("LIX2"u8);
            if (!indexedText && !magic.SequenceEqual("MFT1"u8))
                throw new InvalidDataException("Unknown MiniFire table format. Expected MFT1 or LIX2.");

            var hash = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(4, 8));
            var columns = ReadSchema(source, table, hash, indexedText);
            var reader = new RowReader(data, cancellation);
            return new ConfigTable(columns, indexedText ? reader.ReadTextRows() : reader.ReadRows(columns));
        }

        private static ConfigColumn[] ReadSchema(string source, string table, ulong hash, bool indexedText)
        {
            // ConfTools emits a class per table. Read its schema and field assignments as text.
            var header = Regex.Match(source, $@"(?m)^(?<indent>[ \t]*)public class {Regex.Escape(table)}Item\s*:");
            var end = header.Success
                ? source.IndexOf("\n" + header.Groups["indent"].Value + "}", header.Index + header.Length, StringComparison.Ordinal)
                : -1;
            if (end < 0)
                throw new InvalidDataException($"Configs.cs has no ConfTools schema for {table}.");

            var body = source.Substring(header.Index + header.Length, end - header.Index - header.Length);
            var schema = s_schema.Match(body);
            if (!schema.Success)
                throw new InvalidDataException($"Configs.cs has no binary schema for {table}.");
            var expected = ulong.Parse(schema.Groups[1].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (expected != hash)
                throw new InvalidDataException($"Schema mismatch for {table}. Configs.cs has {expected:X16}, file has {hash:X16}.");

            var key = s_key.Match(body).Groups[1].Value;
            if (indexedText)
            {
                if ((table == "Language" && key == "Key") || (table == "DescTxt" && key == "Id"))
                    return [new(key, ConfigValueType.Int, 0), new("ZH_CN", ConfigValueType.String, 0)];
                throw new InvalidDataException($"Unsupported LIX2 table {table}.");
            }

            var initializer = s_rowInitializer.Match(body);
            if (!initializer.Success)
                throw new InvalidDataException($"Configs.cs has no supported ReadBinary method for {table}.");

            var columns = new List<ConfigColumn>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in initializer.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var field = s_assignment.Match(assignment);
                if (!field.Success || !names.Add(field.Groups[1].Value))
                    throw new InvalidDataException($"Unsupported field in the ReadBinary method for {table}.");

                var type = Enum.Parse<ConfigValueType>(field.Groups[2].Value);
                var depth = field.Groups[3].Value switch
                {
                    "Array" => 1,
                    "Array2" => 2,
                    _ => 0,
                };
                columns.Add(new ConfigColumn(field.Groups[1].Value, type, depth));
            }

            if (columns.Count == 0 || columns[0].Name != key ||
                columns[0] is not { ArrayDepth: 0, Type: ConfigValueType.Int or ConfigValueType.String })
                throw new InvalidDataException($"Invalid primary key in the schema for {table}.");

            return columns.ToArray();
        }

        private sealed class RowReader(byte[] data, CancellationToken cancellation)
        {
            public Dictionary<string, string[]> ReadRows(ConfigColumn[] columns)
            {
                var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
                var count = IntAt(HeaderSize - sizeof(int));
                if (count < 0 || count > (data.Length - _offset) / (columns.Length * sizeof(int)))
                    throw Invalid("Invalid row count.");
                for (var i = 0; i < count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var values = new string[columns.Length];
                    for (var column = 0; column < columns.Length; column++)
                    {
                        var definition = columns[column];
                        if (definition.ArrayDepth == 0)
                            values[column] = ReadScalar(definition.Type);
                        else
                        {
                            var value = new StringBuilder();
                            ReadArray(definition.Type, definition.ArrayDepth, value);
                            values[column] = value.ToString();
                        }
                    }

                    if (values[0] == "null" || !rows.TryAdd(values[0], values))
                        throw Invalid($"Missing or duplicate primary key at row {i + 1}.");
                }

                if (_offset != data.Length)
                    throw Invalid("Unexpected trailing data.");
                return rows;
            }

            public Dictionary<string, string[]> ReadTextRows()
            {
                const int EntrySize = 2 * sizeof(int);
                var count = IntAt(HeaderSize - sizeof(int));
                var available = data.Length - HeaderSize - sizeof(int);
                if (available < 0 || count < 0 || count > available / EntrySize)
                    throw Invalid("Invalid text index row count.");

                var payloadStart = HeaderSize + count * EntrySize + sizeof(int);
                var payloadSize = data.Length - payloadStart;
                if (IntAt(payloadStart - sizeof(int)) != payloadSize || (count == 0 && payloadSize != 0))
                    throw Invalid("Invalid text payload length.");

                var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
                var start = count == 0 ? 0 : IntAt(HeaderSize + sizeof(int));
                if (start != 0)
                    throw Invalid("Invalid first text offset.");

                var previous = 0;
                for (var i = 0; i < count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var id = ReadInt();
                    _offset += sizeof(int);
                    var end = IntAt(_offset + sizeof(int) * (i + 1 < count ? 1 : 0));
                    if ((i > 0 && id <= previous) || end < start || end > payloadSize)
                        throw Invalid($"Invalid text index at row {i + 1}.");

                    var key = id.ToString(CultureInfo.InvariantCulture);
                    rows.Add(key, [key, ReadText(payloadStart + start, end - start)]);
                    start = end;
                    previous = id;
                }

                return rows;
            }

            private int ReadInt()
            {
                if (data.Length - _offset < sizeof(int))
                    throw Invalid("Truncated 32-bit value.");
                var value = IntAt(_offset);
                _offset += sizeof(int);
                return value;
            }

            private int IntAt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));

            private int ReadCount(int itemSize, bool nullable = false)
            {
                var count = ReadInt();
                if (nullable && count == -1)
                    return count;
                if (count < 0 || count > (data.Length - _offset) / itemSize)
                    throw Invalid("Invalid item count.");
                return count;
            }

            private string ReadScalar(ConfigValueType type)
            {
                if (type == ConfigValueType.Int)
                    return ReadInt().ToString(CultureInfo.InvariantCulture);
                if (type == ConfigValueType.Float)
                    return BitConverter.Int32BitsToSingle(ReadInt()).ToString("R", CultureInfo.InvariantCulture);

                var length = ReadCount(1, nullable: true);
                if (length == -1)
                    return "null";
                var text = ReadText(_offset, length);
                _offset += length;
                return text;
            }

            private string ReadText(int offset, int length)
            {
                var text = s_utf8.GetString(data, offset, length);
                return "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
            }

            private void ReadArray(ConfigValueType type, int depth, StringBuilder output)
            {
                var count = ReadCount(sizeof(int), nullable: true);
                if (count == -1)
                {
                    output.Append("null");
                    return;
                }

                output.Append('[');
                for (var i = 0; i < count; i++)
                {
                    if ((i & 255) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    if (i != 0)
                        output.Append(", ");
                    if (depth == 1)
                        output.Append(ReadScalar(type));
                    else
                        ReadArray(type, depth - 1, output);
                }
                output.Append(']');
            }

            private InvalidDataException Invalid(string reason) => new($"Byte {_offset}: {reason}");

            private int _offset = HeaderSize;
        }

        private const int HeaderSize = 16;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private static readonly Regex s_schema = new(@"\bpublic const ulong BinarySchema\s*=\s*0x([0-9a-fA-F]{1,16})UL\s*;");
        private static readonly Regex s_key = new(@"\bGetIndex\(\)\s*=>\s*(\w+)\s*;");
        private static readonly Regex s_rowInitializer = new(@"\bReadBinary\(ConfigBinaryReader reader\)\s*\{\s*return new \w+\s*\{([^{}]*)\}\s*;");
        private static readonly Regex s_assignment = new(@"^(\w+)\s*=\s*reader\.Read(Int|Float|String)(Array2|Array)?\(\)$");
    }
}
