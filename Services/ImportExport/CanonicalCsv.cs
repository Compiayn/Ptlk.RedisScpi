using System.Text;

namespace Ptlk.RedisScpi.Services.ImportExport;

internal sealed record CanonicalCsvRecord(int Line, IReadOnlyList<string> Fields);

internal sealed record CanonicalCsvDocument(
    IReadOnlyList<string> Headers,
    IReadOnlyList<CanonicalCsvRecord> Records);

internal sealed class CanonicalCsvException(int line, string message) : Exception(message)
{
    public int Line { get; } = line;
}

internal static class CanonicalCsv
{
    public static void AppendRecord(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            AppendField(builder, value ?? string.Empty);
            first = false;
        }

        builder.Append("\r\n");
    }

    public static CanonicalCsvDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parsedRecords = ParseRecords(content);
        var nonBlankRecords = parsedRecords
            .Where(record => !IsBlank(record.Fields))
            .ToList();

        if (nonBlankRecords.Count == 0)
        {
            throw new CanonicalCsvException(1, "CSV is empty or does not contain a header row.");
        }

        var rawHeaders = nonBlankRecords[0].Fields.ToArray();
        if (rawHeaders.Length > 0)
        {
            rawHeaders[0] = rawHeaders[0].TrimStart('\uFEFF');
        }

        var headers = rawHeaders.Select(header => header.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new CanonicalCsvException(nonBlankRecords[0].Line, "CSV contains an empty header name.");
        }

        var duplicateHeader = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader is not null)
        {
            throw new CanonicalCsvException(
                nonBlankRecords[0].Line,
                $"CSV contains duplicate header '{duplicateHeader.Key}'.");
        }

        var records = new List<CanonicalCsvRecord>();
        foreach (var record in nonBlankRecords.Skip(1))
        {
            if (record.Fields.Count > headers.Length)
            {
                throw new CanonicalCsvException(
                    record.Line,
                    $"CSV row has {record.Fields.Count} fields but the header has {headers.Length}.");
            }

            var fields = record.Fields.ToList();
            while (fields.Count < headers.Length)
            {
                fields.Add(string.Empty);
            }

            records.Add(new CanonicalCsvRecord(record.Line, fields));
        }

        return new CanonicalCsvDocument(headers, records);
    }

    private static List<CanonicalCsvRecord> ParseRecords(string content)
    {
        var records = new List<CanonicalCsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordStartLine = 1;
        var inQuotes = false;
        var afterClosingQuote = false;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];

            if (inQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                        afterClosingQuote = true;
                    }

                    continue;
                }

                if (current == '\r')
                {
                    field.Append('\r');
                    if (index + 1 < content.Length && content[index + 1] == '\n')
                    {
                        field.Append('\n');
                        index++;
                    }

                    line++;
                    continue;
                }

                field.Append(current);
                if (current == '\n')
                {
                    line++;
                }

                continue;
            }

            if (afterClosingQuote)
            {
                if (current == ',')
                {
                    CompleteField(fields, field);
                    afterClosingQuote = false;
                    continue;
                }

                if (current is '\r' or '\n')
                {
                    CompleteField(fields, field);
                    CompleteRecord(records, fields, recordStartLine);
                    ConsumeLineEnding(content, ref index, current);
                    line++;
                    recordStartLine = line;
                    afterClosingQuote = false;
                    continue;
                }

                throw new CanonicalCsvException(
                    line,
                    "Only a comma, line ending, or end of file may follow a closing quote.");
            }

            if (current == '"')
            {
                if (field.Length != 0)
                {
                    throw new CanonicalCsvException(line, "A quoted CSV field must start with a quote.");
                }

                inQuotes = true;
                continue;
            }

            if (current == ',')
            {
                CompleteField(fields, field);
                continue;
            }

            if (current is '\r' or '\n')
            {
                CompleteField(fields, field);
                CompleteRecord(records, fields, recordStartLine);
                ConsumeLineEnding(content, ref index, current);
                line++;
                recordStartLine = line;
                continue;
            }

            field.Append(current);
        }

        if (inQuotes)
        {
            throw new CanonicalCsvException(recordStartLine, "CSV contains an unterminated quoted field.");
        }

        if (afterClosingQuote || field.Length > 0 || fields.Count > 0)
        {
            CompleteField(fields, field);
            CompleteRecord(records, fields, recordStartLine);
        }

        return records;
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    private static void CompleteField(List<string> fields, StringBuilder field)
    {
        fields.Add(field.ToString());
        field.Clear();
    }

    private static void CompleteRecord(
        List<CanonicalCsvRecord> records,
        List<string> fields,
        int recordStartLine)
    {
        records.Add(new CanonicalCsvRecord(recordStartLine, fields.ToArray()));
        fields.Clear();
    }

    private static void ConsumeLineEnding(string content, ref int index, char current)
    {
        if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
        {
            index++;
        }
    }

    private static bool IsBlank(IReadOnlyList<string> fields) =>
        fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace);
}
