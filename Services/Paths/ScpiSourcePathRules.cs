namespace Ptlk.RedisScpi.Services.Paths;

public static class ScpiSourcePathRules
{
    public const string Prefix = "scpi:";
    public const int MaximumTokenLength = 160;

    public static bool IsSafeToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumTokenLength
        && value.All(IsSafeCharacter);

    private static bool IsSafeCharacter(char character) =>
        character is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '-' or '_' or '.';

    public static bool TryParsePointSourcePath(
        string? sourcePath,
        out string endpointId,
        out string pointId)
    {
        endpointId = "";
        pointId = "";
        if (string.IsNullOrEmpty(sourcePath)
            || !sourcePath.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = sourcePath[Prefix.Length..];
        var separator = payload.IndexOf('/');
        if (separator <= 0
            || separator == payload.Length - 1
            || payload.IndexOf('/', separator + 1) >= 0)
        {
            return false;
        }

        var endpoint = payload[..separator];
        var point = payload[(separator + 1)..];
        if (!IsSafeToken(endpoint) || !IsSafeToken(point))
        {
            return false;
        }

        endpointId = endpoint;
        pointId = point;
        return true;
    }
}
