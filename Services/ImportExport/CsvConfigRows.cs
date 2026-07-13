using System.Globalization;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Services.Paths;

namespace Ptlk.RedisScpi.Services.ImportExport;

internal static class ScpiConfigCsvSchema
{
    public const string EndpointKind = "endpoint";
    public const string PointKind = "point";
    public const string EnumOptionKind = "enum_option";
    public const string MappingKind = "mapping";

    public static readonly string[] Headers =
    [
        "kind",
        "endpoint_id",
        "endpoint_display_name",
        "endpoint_enabled",
        "transport",
        "tcp_host",
        "tcp_port",
        "timeout_ms",
        "endpoint_polling_interval_ms",
        "converter_id",
        "error_check_mode",
        "error_queue_query",
        "command_terminator",
        "response_terminator",
        "point_id",
        "point_name",
        "point_display_name",
        "point_enabled",
        "access",
        "data_type",
        "number_type",
        "string_format",
        "enum_format",
        "read_template",
        "write_template",
        "unit",
        "point_polling_enabled",
        "point_polling_interval_ms",
        "initial_read",
        "enum_display_name",
        "enum_value",
        "enum_code",
        "enum_sort_order",
        "mapping_source_path",
        "mapping_redis_key"
    ];
}

internal sealed record EndpointImportRow(
    int Line,
    string EndpointId,
    string DisplayName,
    bool Enabled,
    string Transport,
    string TcpHost,
    int TcpPort,
    int TimeoutMs,
    int PollingIntervalMs,
    string ConverterId,
    string ErrorCheckMode,
    string ErrorQueueQuery,
    string CommandTerminator,
    string ResponseTerminator);

internal sealed record PointImportRow(
    int Line,
    string EndpointId,
    string PointId,
    string Name,
    string? DisplayName,
    bool Enabled,
    string Access,
    string DataType,
    string? NumberType,
    string? StringFormat,
    string? EnumFormat,
    string ReadTemplate,
    string? WriteTemplate,
    string? Unit,
    bool PollingEnabled,
    int? PollingIntervalMs,
    bool InitialRead);

internal sealed record EnumOptionImportRow(
    int Line,
    string EndpointId,
    string PointId,
    string DisplayName,
    string Value,
    int Code,
    int SortOrder);

internal sealed record MappingImportRow(
    int Line,
    string SourcePath,
    string RedisKey);

internal sealed record ParsedScpiConfigImport(
    IReadOnlyList<EndpointImportRow> Endpoints,
    IReadOnlyList<PointImportRow> Points,
    IReadOnlyList<EnumOptionImportRow> EnumOptions,
    IReadOnlyList<MappingImportRow> Mappings,
    IReadOnlyList<string> Errors)
{
    public int RowCount => Endpoints.Count + Points.Count + EnumOptions.Count + Mappings.Count;
}

internal static class ScpiConfigCsvRowParser
{
    public static ParsedScpiConfigImport Parse(CanonicalCsvDocument document)
    {
        var errors = new List<string>();
        var endpoints = new List<EndpointImportRow>();
        var points = new List<PointImportRow>();
        var enumOptions = new List<EnumOptionImportRow>();
        var mappings = new List<MappingImportRow>();
        var headerIndex = document.Headers
            .Select((header, index) => (header, index))
            .ToDictionary(item => item.header, item => item.index, StringComparer.OrdinalIgnoreCase);

        if (!headerIndex.ContainsKey("kind"))
        {
            errors.Add("Line 1: required header 'kind' is missing.");
            return new ParsedScpiConfigImport(endpoints, points, enumOptions, mappings, errors);
        }

        foreach (var record in document.Records)
        {
            var row = new ImportRecord(record.Line, headerIndex, record.Fields);
            var kind = row.Get("kind").Trim().ToLowerInvariant();
            switch (kind)
            {
                case ScpiConfigCsvSchema.EndpointKind:
                    if (TryParseEndpoint(row, errors, out var endpoint))
                    {
                        endpoints.Add(endpoint);
                    }
                    break;

                case ScpiConfigCsvSchema.PointKind:
                    if (TryParsePoint(row, errors, out var point))
                    {
                        points.Add(point);
                    }
                    break;

                case ScpiConfigCsvSchema.EnumOptionKind:
                    if (TryParseEnumOption(row, errors, out var enumOption))
                    {
                        enumOptions.Add(enumOption);
                    }
                    break;

                case ScpiConfigCsvSchema.MappingKind:
                    if (TryParseMapping(row, errors, out var mapping))
                    {
                        mappings.Add(mapping);
                    }
                    break;

                case "":
                    errors.Add($"Line {row.Line}: kind is required.");
                    break;

                default:
                    errors.Add($"Line {row.Line}: unsupported kind '{row.Get("kind")}'.");
                    break;
            }
        }

        ValidateDuplicateRows(endpoints, points, enumOptions, mappings, errors);
        return new ParsedScpiConfigImport(endpoints, points, enumOptions, mappings, errors);
    }

    private static bool TryParseEndpoint(
        ImportRecord row,
        List<string> errors,
        out EndpointImportRow result)
    {
        var startErrorCount = errors.Count;
        var endpointId = RequiredTrimmed(row, "endpoint_id", errors, 160);
        var displayName = RequiredTrimmed(row, "endpoint_display_name", errors, 160);
        var enabled = RequiredBool(row, "endpoint_enabled", errors);
        var transport = RequiredTrimmed(row, "transport", errors, 32).ToLowerInvariant();
        var tcpHost = RequiredTrimmed(row, "tcp_host", errors, 255);
        var tcpPort = RequiredInt(row, "tcp_port", errors, 1, 65535);
        var timeoutMs = RequiredInt(row, "timeout_ms", errors, 1, int.MaxValue);
        var pollingIntervalMs = RequiredInt(row, "endpoint_polling_interval_ms", errors, 100, int.MaxValue);
        var converterId = RequiredTrimmed(row, "converter_id", errors, 160);
        var errorCheckMode = RequiredTrimmed(row, "error_check_mode", errors, 32).ToLowerInvariant();
        var errorQueueQuery = RequiredTrimmed(row, "error_queue_query", errors, 1000);
        var commandTerminator = RequiredExact(row, "command_terminator", errors, 16);
        var responseTerminator = RequiredExact(row, "response_terminator", errors, 16);

        if (!IsSafeId(endpointId))
        {
            errors.Add($"Line {row.Line}: endpoint_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        if (!transport.Equals("tcp", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Line {row.Line}: transport must be 'tcp'.");
        }

        if (!IsSafeId(converterId))
        {
            errors.Add($"Line {row.Line}: converter_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        if (!ScpiErrorCheckModes.IsValid(errorCheckMode))
        {
            errors.Add($"Line {row.Line}: error_check_mode must be none, after-write, or after-command.");
        }

        if (commandTerminator.Contains('\0'))
        {
            errors.Add($"Line {row.Line}: command_terminator must not contain a null character.");
        }

        if (responseTerminator.Contains('\0'))
        {
            errors.Add($"Line {row.Line}: response_terminator must not contain a null character.");
        }

        result = new EndpointImportRow(
            row.Line,
            endpointId,
            displayName,
            enabled ?? false,
            transport,
            tcpHost,
            tcpPort ?? 0,
            timeoutMs ?? 0,
            pollingIntervalMs ?? 0,
            converterId,
            errorCheckMode,
            errorQueueQuery,
            commandTerminator,
            responseTerminator);
        return errors.Count == startErrorCount;
    }

    private static bool TryParsePoint(
        ImportRecord row,
        List<string> errors,
        out PointImportRow result)
    {
        var startErrorCount = errors.Count;
        var endpointId = RequiredTrimmed(row, "endpoint_id", errors, 160);
        var pointId = RequiredTrimmed(row, "point_id", errors, 160);
        var name = RequiredTrimmed(row, "point_name", errors, 160);
        var displayName = OptionalTrimmed(row, "point_display_name", errors, 160);
        var enabled = RequiredBool(row, "point_enabled", errors);
        var access = RequiredTrimmed(row, "access", errors, 16).ToLowerInvariant();
        var dataType = RequiredTrimmed(row, "data_type", errors, 16).ToLowerInvariant();
        var numberType = OptionalTrimmed(row, "number_type", errors, 16)?.ToLowerInvariant();
        var stringFormat = OptionalTrimmed(row, "string_format", errors, 16)?.ToLowerInvariant();
        var enumFormat = OptionalTrimmed(row, "enum_format", errors, 16)?.ToLowerInvariant();
        var readTemplate = RequiredTrimmed(row, "read_template", errors, 1000);
        var writeTemplate = OptionalTrimmed(row, "write_template", errors, 1000);
        var unit = OptionalTrimmed(row, "unit", errors, 80);
        var pollingEnabled = RequiredBool(row, "point_polling_enabled", errors);
        var pointPollingInterval = OptionalInt(row, "point_polling_interval_ms", errors, 100, int.MaxValue);
        var initialRead = RequiredBool(row, "initial_read", errors);

        if (!IsSafeId(endpointId))
        {
            errors.Add($"Line {row.Line}: endpoint_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        if (!IsSafeId(pointId))
        {
            errors.Add($"Line {row.Line}: point_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        if (!ScpiAccessModes.IsValid(access))
        {
            errors.Add($"Line {row.Line}: access must be readonly or readwrite.");
        }

        var validDataType = ScpiDataTypes.IsValid(dataType);
        if (!validDataType)
        {
            errors.Add($"Line {row.Line}: data_type must be number, string, or enum.");
        }

        if (pollingEnabled == true && enabled != true)
        {
            errors.Add($"Line {row.Line}: point_polling_enabled requires point_enabled to be true.");
        }

        if (validDataType)
        {
            ValidateTypeSpecificFields(row.Line, dataType, numberType, stringFormat, enumFormat, errors);
        }

        ValidateTemplate(row.Line, "read_template", readTemplate, allowValue: false, requireValue: false, errors);
        if (access.Equals(ScpiAccessModes.Readwrite, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(writeTemplate))
            {
                errors.Add($"Line {row.Line}: write_template is required for a readwrite point.");
            }
            else
            {
                ValidateTemplate(row.Line, "write_template", writeTemplate, allowValue: true, requireValue: true, errors);
            }
        }
        else if (!string.IsNullOrEmpty(writeTemplate))
        {
            errors.Add($"Line {row.Line}: write_template must be empty for a readonly point.");
        }

        result = new PointImportRow(
            row.Line,
            endpointId,
            pointId,
            name,
            displayName,
            enabled ?? false,
            access,
            dataType,
            numberType,
            stringFormat,
            enumFormat,
            readTemplate,
            writeTemplate,
            unit,
            pollingEnabled ?? false,
            pointPollingInterval,
            initialRead ?? false);
        return errors.Count == startErrorCount;
    }

    private static bool TryParseEnumOption(
        ImportRecord row,
        List<string> errors,
        out EnumOptionImportRow result)
    {
        var startErrorCount = errors.Count;
        var endpointId = RequiredTrimmed(row, "endpoint_id", errors, 160);
        var pointId = RequiredTrimmed(row, "point_id", errors, 160);
        var displayName = RequiredTrimmed(row, "enum_display_name", errors, 160);
        var value = RequiredTrimmed(row, "enum_value", errors, 320);
        var code = RequiredInt(row, "enum_code", errors, int.MinValue, int.MaxValue);
        var sortOrder = RequiredInt(row, "enum_sort_order", errors, int.MinValue, int.MaxValue);

        if (!IsSafeId(endpointId))
        {
            errors.Add($"Line {row.Line}: endpoint_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        if (!IsSafeId(pointId))
        {
            errors.Add($"Line {row.Line}: point_id must use 1-160 ASCII letters, numbers, '-', '_', or '.'.");
        }

        result = new EnumOptionImportRow(
            row.Line,
            endpointId,
            pointId,
            displayName,
            value,
            code ?? 0,
            sortOrder ?? 0);
        return errors.Count == startErrorCount;
    }

    private static bool TryParseMapping(
        ImportRecord row,
        List<string> errors,
        out MappingImportRow result)
    {
        var startErrorCount = errors.Count;
        var sourcePath = RequiredTrimmed(row, "mapping_source_path", errors, 320);
        var redisKey = RequiredTrimmed(row, "mapping_redis_key", errors, 320);

        if (!IsValidSourcePathShape(sourcePath))
        {
            errors.Add($"Line {row.Line}: mapping_source_path must use scpi:{{endpointId}}/{{pointId}}.");
        }

        if (!redisKey.StartsWith("point:", StringComparison.Ordinal))
        {
            errors.Add($"Line {row.Line}: mapping_redis_key must start with 'point:'.");
        }
        else if (redisKey.Length == "point:".Length)
        {
            errors.Add($"Line {row.Line}: mapping_redis_key must include a point path after 'point:'.");
        }

        result = new MappingImportRow(row.Line, sourcePath, redisKey);
        return errors.Count == startErrorCount;
    }

    private static void ValidateTypeSpecificFields(
        int line,
        string dataType,
        string? numberType,
        string? stringFormat,
        string? enumFormat,
        List<string> errors)
    {
        switch (dataType)
        {
            case ScpiDataTypes.Number:
                if (!ScpiNumberTypes.IsValid(numberType))
                {
                    errors.Add($"Line {line}: number_type must be int or double for a number point.");
                }
                if (stringFormat is not null || enumFormat is not null)
                {
                    errors.Add($"Line {line}: string_format and enum_format must be empty for a number point.");
                }
                break;

            case ScpiDataTypes.String:
                if (!ScpiStringFormats.IsValid(stringFormat))
                {
                    errors.Add($"Line {line}: string_format must be raw or quoted for a string point.");
                }
                if (numberType is not null || enumFormat is not null)
                {
                    errors.Add($"Line {line}: number_type and enum_format must be empty for a string point.");
                }
                break;

            case ScpiDataTypes.Enum:
                if (!ScpiEnumFormats.IsValid(enumFormat))
                {
                    errors.Add($"Line {line}: enum_format must be value or code for an enum point.");
                }
                if (numberType is not null || stringFormat is not null)
                {
                    errors.Add($"Line {line}: number_type and string_format must be empty for an enum point.");
                }
                break;
        }
    }

    private static void ValidateTemplate(
        int line,
        string fieldName,
        string template,
        bool allowValue,
        bool requireValue,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        var foundValue = false;
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == '}')
            {
                errors.Add($"Line {line}: {fieldName} contains an unmatched closing brace.");
                return;
            }

            if (template[index] != '{')
            {
                continue;
            }

            var close = template.IndexOf('}', index + 1);
            if (close < 0)
            {
                errors.Add($"Line {line}: {fieldName} contains an unmatched opening brace.");
                return;
            }

            var variable = template[(index + 1)..close];
            if (variable.Equals("value", StringComparison.Ordinal))
            {
                foundValue = true;
                if (!allowValue)
                {
                    errors.Add($"Line {line}: {fieldName} must not contain '{{value}}'.");
                }
            }
            else if (!variable.Equals("name", StringComparison.Ordinal))
            {
                errors.Add($"Line {line}: {fieldName} contains unsupported template variable '{{{variable}}}'.");
            }

            index = close;
        }

        if (requireValue && !foundValue)
        {
            errors.Add($"Line {line}: {fieldName} must contain '{{value}}'.");
        }
    }

    private static void ValidateDuplicateRows(
        IReadOnlyList<EndpointImportRow> endpoints,
        IReadOnlyList<PointImportRow> points,
        IReadOnlyList<EnumOptionImportRow> enumOptions,
        IReadOnlyList<MappingImportRow> mappings,
        List<string> errors)
    {
        AddDuplicateErrors(endpoints, row => row.Line, row => row.EndpointId, "endpoint_id", errors, StringComparer.OrdinalIgnoreCase);
        AddDuplicateErrors(points, row => row.Line, row => Composite(row.EndpointId, row.PointId), "endpoint_id + point_id", errors, StringComparer.OrdinalIgnoreCase);
        AddDuplicateErrors(mappings, row => row.Line, row => row.SourcePath, "mapping_source_path", errors, StringComparer.OrdinalIgnoreCase);
        AddDuplicateErrors(mappings, row => row.Line, row => row.RedisKey, "mapping_redis_key", errors);

        foreach (var group in enumOptions.GroupBy(row => Composite(row.EndpointId, row.PointId), StringComparer.OrdinalIgnoreCase))
        {
            AddDuplicateErrors(group.ToList(), row => row.Line, row => row.Value, "enum_value", errors, StringComparer.OrdinalIgnoreCase);
            AddDuplicateErrors(group.ToList(), row => row.Line, row => row.Code.ToString(CultureInfo.InvariantCulture), "enum_code", errors);
        }
    }

    private static void AddDuplicateErrors<T>(
        IReadOnlyList<T> rows,
        Func<T, int> lineSelector,
        Func<T, string> keySelector,
        string fieldName,
        List<string> errors,
        StringComparer? comparer = null)
        where T : notnull
    {
        comparer ??= StringComparer.Ordinal;
        var firstLines = new Dictionary<string, int>(comparer);
        foreach (var row in rows)
        {
            var line = lineSelector(row);
            var key = keySelector(row);
            if (firstLines.TryGetValue(key, out var firstLine))
            {
                errors.Add($"Line {line}: duplicate {fieldName} '{key}' (first declared on line {firstLine}).");
            }
            else
            {
                firstLines[key] = line;
            }
        }
    }

    private static string RequiredTrimmed(
        ImportRecord row,
        string field,
        List<string> errors,
        int maxLength)
    {
        var value = row.Get(field).Trim();
        if (value.Length == 0)
        {
            errors.Add($"Line {row.Line}: {field} is required.");
        }
        else if (value.Length > maxLength)
        {
            errors.Add($"Line {row.Line}: {field} exceeds {maxLength} characters.");
        }

        return value;
    }

    private static string RequiredExact(
        ImportRecord row,
        string field,
        List<string> errors,
        int maxLength)
    {
        var value = row.Get(field);
        if (value.Length == 0)
        {
            errors.Add($"Line {row.Line}: {field} is required.");
        }
        else if (value.Length > maxLength)
        {
            errors.Add($"Line {row.Line}: {field} exceeds {maxLength} characters.");
        }

        return value;
    }

    private static string? OptionalTrimmed(
        ImportRecord row,
        string field,
        List<string> errors,
        int maxLength)
    {
        var value = row.Get(field).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add($"Line {row.Line}: {field} exceeds {maxLength} characters.");
        }

        return value;
    }

    private static bool? RequiredBool(ImportRecord row, string field, List<string> errors)
    {
        var value = row.Get(field).Trim();
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add($"Line {row.Line}: {field} must be true or false.");
        return null;
    }

    private static int? RequiredInt(
        ImportRecord row,
        string field,
        List<string> errors,
        int minimum,
        int maximum)
    {
        var value = row.Get(field).Trim();
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"Line {row.Line}: {field} must be an integer.");
            return null;
        }

        if (parsed < minimum || parsed > maximum)
        {
            errors.Add($"Line {row.Line}: {field} must be between {minimum} and {maximum}.");
            return null;
        }

        return parsed;
    }

    private static int? OptionalInt(
        ImportRecord row,
        string field,
        List<string> errors,
        int minimum,
        int maximum)
    {
        var value = row.Get(field).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"Line {row.Line}: {field} must be an integer when provided.");
            return null;
        }

        if (parsed < minimum || parsed > maximum)
        {
            errors.Add($"Line {row.Line}: {field} must be between {minimum} and {maximum} when provided.");
            return null;
        }

        return parsed;
    }

    private static bool IsSafeId(string value) => ScpiSourcePathRules.IsSafeToken(value);

    private static bool IsValidSourcePathShape(string sourcePath) =>
        ScpiSourcePathRules.TryParsePointSourcePath(sourcePath, out _, out _);

    private static string Composite(string left, string right) => $"{left}\u001F{right}";

    private sealed class ImportRecord(
        int line,
        IReadOnlyDictionary<string, int> headerIndex,
        IReadOnlyList<string> fields)
    {
        public int Line { get; } = line;

        public string Get(string name) =>
            headerIndex.TryGetValue(name, out var index) && index < fields.Count
                ? fields[index]
                : string.Empty;
    }
}
