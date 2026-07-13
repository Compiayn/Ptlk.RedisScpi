using System.Text.RegularExpressions;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class ScpiErrorQueueService
{
    private static readonly Regex ResponsePattern = new(
        @"^\s*(?<code>[+-]?\d+)\s*(?:,\s*(?<message>.*))?$",
        RegexOptions.Compiled);

    public async Task<ScpiErrorQueueResult> QueryAsync(
        IScpiTransport transport,
        ScpiEndpointConfig endpoint,
        CancellationToken cancellationToken = default)
    {
        var raw = await transport.QueryAsync(endpoint, endpoint.ErrorQueueQuery, cancellationToken);
        try
        {
            return Parse(raw);
        }
        catch (ScpiParseException ex)
        {
            throw new ScpiOperationException(ex.ErrorCode, ex.Message, null, raw, ex);
        }
    }

    public ScpiErrorQueueResult Parse(string rawResponse)
    {
        var normalized = ScpiValueConverter.NormalizeResponse(rawResponse);
        var match = ResponsePattern.Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups["code"].Value, out var code))
        {
            throw new ScpiParseException($"SCPI Error Queue response '{normalized}' is invalid.", rawResponse);
        }

        var message = match.Groups["message"].Success
            ? Unquote(match.Groups["message"].Value.Trim())
            : "";
        return new ScpiErrorQueueResult(code == 0, code, message, rawResponse);
    }

    public static void ThrowIfError(ScpiErrorQueueResult result)
    {
        if (!result.Success)
        {
            throw new ScpiInstrumentException(
                $"SCPI instrument error {result.Code}: {result.Message}",
                result.RawResponse);
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : value;
}
