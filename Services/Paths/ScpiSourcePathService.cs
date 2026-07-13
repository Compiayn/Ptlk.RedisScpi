namespace Ptlk.RedisScpi.Services.Paths;

public sealed class ScpiSourcePathService
{
    public const string Prefix = ScpiSourcePathRules.Prefix;

    public bool IsSafeToken(string? value) => ScpiSourcePathRules.IsSafeToken(value);

    public string NormalizeToken(string value, string fieldName)
    {
        var normalized = value?.Trim() ?? "";
        if (!IsSafeToken(normalized))
        {
            throw new InvalidOperationException(
                $"{fieldName} must be 1-{ScpiSourcePathRules.MaximumTokenLength} characters and contain only ASCII letters, numbers, '-', '_', or '.'.");
        }

        return normalized;
    }

    public string BuildPointSourcePath(string endpointId, string pointId)
    {
        var normalizedEndpointId = NormalizeToken(endpointId, "EndpointId");
        var normalizedPointId = NormalizeToken(pointId, "PointId");
        return $"{Prefix}{normalizedEndpointId}/{normalizedPointId}";
    }

    public bool TryParsePointSourcePath(
        string? sourcePath,
        out string endpointId,
        out string pointId) =>
        ScpiSourcePathRules.TryParsePointSourcePath(sourcePath, out endpointId, out pointId);
}
