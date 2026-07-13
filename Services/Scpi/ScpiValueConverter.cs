using System.Globalization;
using System.Text.Json;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class ScpiValueConverter
{
    public JsonElement ParseUserInput(ScpiPointConfig point, string input)
    {
        if (ScpiDataTypes.Number.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            if (ScpiNumberTypes.Int.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return JsonSerializer.SerializeToElement(integer);
            }

            if (ScpiNumberTypes.Double.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number))
            {
                return JsonSerializer.SerializeToElement(number);
            }

            throw InvalidType(point, point.NumberType is "int" ? "an integer" : "a finite number");
        }

        if (ScpiDataTypes.Enum.Equals(point.DataType, StringComparison.OrdinalIgnoreCase)
            && ScpiEnumFormats.Code.Equals(point.EnumFormat, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                throw InvalidType(point, "a configured enum integer code");
            }

            return JsonSerializer.SerializeToElement(code);
        }

        return JsonSerializer.SerializeToElement(input);
    }

    public ScpiConvertedValue ParseResponse(ScpiPointConfig point, string response)
    {
        var normalized = NormalizeResponse(response);
        if (ScpiDataTypes.Number.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            return ParseNumberResponse(point, normalized);
        }

        if (ScpiDataTypes.String.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            var value = ScpiStringFormats.Quoted.Equals(point.StringFormat, StringComparison.OrdinalIgnoreCase)
                ? Unquote(normalized)
                : normalized;
            return new ScpiConvertedValue(
                value,
                value,
                JsonSerializer.SerializeToElement(value),
                FormatStringForScpi(point, value));
        }

        if (ScpiDataTypes.Enum.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            var enumText = Unquote(normalized);
            var option = point.EnumOptions.FirstOrDefault(item =>
                item.Value.Equals(enumText, StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                throw new ScpiValidationException(
                    ScpiErrorCodes.EnumValueNotFound,
                    $"SCPI enum response '{normalized}' is not configured for '{point.SourcePath}'.");
            }

            return FromEnumOption(point, option);
        }

        throw new ScpiValidationException(
            ScpiErrorCodes.ConfigurationInvalid,
            $"Unsupported SCPI data type '{point.DataType}'.");
    }

    public ScpiConvertedValue ConvertInput(ScpiPointConfig point, JsonElement input)
    {
        if (ScpiDataTypes.Number.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            if (input.ValueKind != JsonValueKind.Number)
            {
                throw InvalidType(point, "a JSON number");
            }

            if (ScpiNumberTypes.Int.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase))
            {
                if (!input.TryGetInt64(out var integer))
                {
                    throw InvalidType(point, "an integer JSON number");
                }

                var text = integer.ToString(CultureInfo.InvariantCulture);
                return new ScpiConvertedValue(integer, text, JsonSerializer.SerializeToElement(integer), text);
            }

            if (ScpiNumberTypes.Double.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase)
                && input.TryGetDouble(out var number)
                && double.IsFinite(number))
            {
                var text = number.ToString("R", CultureInfo.InvariantCulture);
                return new ScpiConvertedValue(number, text, JsonSerializer.SerializeToElement(number), text);
            }

            throw InvalidType(point, "a finite JSON number");
        }

        if (ScpiDataTypes.String.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            if (input.ValueKind != JsonValueKind.String)
            {
                throw InvalidType(point, "a JSON string");
            }

            var value = input.GetString() ?? "";
            return new ScpiConvertedValue(
                value,
                value,
                JsonSerializer.SerializeToElement(value),
                FormatStringForScpi(point, value));
        }

        if (ScpiDataTypes.Enum.Equals(point.DataType, StringComparison.OrdinalIgnoreCase))
        {
            ScpiEnumOption? option;
            if (ScpiEnumFormats.Value.Equals(point.EnumFormat, StringComparison.OrdinalIgnoreCase))
            {
                if (input.ValueKind != JsonValueKind.String)
                {
                    throw InvalidType(point, "a configured enum string value");
                }

                var requested = input.GetString() ?? "";
                option = point.EnumOptions.FirstOrDefault(item =>
                    item.Value.Equals(requested, StringComparison.OrdinalIgnoreCase));
                if (option is null)
                {
                    throw new ScpiValidationException(
                        ScpiErrorCodes.EnumValueNotFound,
                        $"Enum value '{requested}' is not configured for '{point.SourcePath}'.");
                }
            }
            else if (ScpiEnumFormats.Code.Equals(point.EnumFormat, StringComparison.OrdinalIgnoreCase))
            {
                if (input.ValueKind != JsonValueKind.Number || !input.TryGetInt32(out var code))
                {
                    throw InvalidType(point, "a configured enum integer code");
                }

                option = point.EnumOptions.FirstOrDefault(item => item.Code == code);
                if (option is null)
                {
                    throw new ScpiValidationException(
                        ScpiErrorCodes.EnumCodeNotFound,
                        $"Enum code '{code}' is not configured for '{point.SourcePath}'.");
                }
            }
            else
            {
                throw new ScpiValidationException(
                    ScpiErrorCodes.ConfigurationInvalid,
                    $"Enum format is invalid for '{point.SourcePath}'.");
            }

            return FromEnumOption(point, option);
        }

        throw new ScpiValidationException(
            ScpiErrorCodes.ConfigurationInvalid,
            $"Unsupported SCPI data type '{point.DataType}'.");
    }

    public bool AreEqual(ScpiConvertedValue expected, ScpiConvertedValue actual) =>
        expected.Value switch
        {
            double expectedDouble when actual.Value is double actualDouble => expectedDouble.Equals(actualDouble),
            _ => Equals(expected.Value, actual.Value)
        };

    public static string NormalizeResponse(string value) =>
        value.Trim().TrimEnd('\r', '\n');

    private static ScpiConvertedValue ParseNumberResponse(ScpiPointConfig point, string normalized)
    {
        if (ScpiNumberTypes.Int.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            var text = integer.ToString(CultureInfo.InvariantCulture);
            return new ScpiConvertedValue(integer, text, JsonSerializer.SerializeToElement(integer), text);
        }

        if (ScpiNumberTypes.Double.Equals(point.NumberType, StringComparison.OrdinalIgnoreCase)
            && double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            var text = number.ToString("R", CultureInfo.InvariantCulture);
            return new ScpiConvertedValue(number, text, JsonSerializer.SerializeToElement(number), text);
        }

        throw new ScpiParseException(
            $"SCPI response '{normalized}' cannot be parsed as {point.NumberType ?? "number"} for '{point.SourcePath}'.");
    }

    private static ScpiConvertedValue FromEnumOption(ScpiPointConfig point, ScpiEnumOption option)
    {
        if (ScpiEnumFormats.Code.Equals(point.EnumFormat, StringComparison.OrdinalIgnoreCase))
        {
            var redisText = option.Code.ToString(CultureInfo.InvariantCulture);
            return new ScpiConvertedValue(
                option.Code,
                redisText,
                JsonSerializer.SerializeToElement(option.Code),
                option.Value);
        }

        return new ScpiConvertedValue(
            option.Value,
            option.Value,
            JsonSerializer.SerializeToElement(option.Value),
            option.Value);
    }

    private static string FormatStringForScpi(ScpiPointConfig point, string value) =>
        ScpiStringFormats.Quoted.Equals(point.StringFormat, StringComparison.OrdinalIgnoreCase)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            var body = value[1..^1];
            return value[0] == '"'
                ? body.Replace("\"\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                : body.Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }

    private static ScpiValidationException InvalidType(ScpiPointConfig point, string expected) =>
        new(
            ScpiErrorCodes.InvalidValueType,
            $"Point '{point.SourcePath}' requires {expected}.");
}
