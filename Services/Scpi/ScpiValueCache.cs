using System.Collections.Concurrent;
using System.Text.Json;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed record ScpiCachedValue(
    string SourcePath,
    string EndpointId,
    string PointId,
    JsonElement? Value,
    string? RedisValue,
    string Quality,
    DateTimeOffset UpdatedAt,
    string Operation,
    string? RawResponse,
    string? ErrorCode,
    string? ErrorMessage,
    bool Stale,
    string? StaleReason);

public sealed class ScpiValueCache
{
    private readonly ConcurrentDictionary<string, ScpiCachedValue> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public void Set(ScpiCachedValue value)
    {
        _values[value.SourcePath] = value;
        Changed?.Invoke();
    }

    public void SetGood(
        string sourcePath,
        string endpointId,
        string pointId,
        JsonElement value,
        string redisValue,
        string operation,
        string? rawResponse)
    {
        Set(new ScpiCachedValue(
            sourcePath,
            endpointId,
            pointId,
            value.Clone(),
            redisValue,
            Contracts.Scpi.ScpiQuality.Good,
            DateTimeOffset.UtcNow,
            operation,
            rawResponse,
            null,
            null,
            false,
            null));
    }

    public void SetBad(
        string sourcePath,
        string endpointId,
        string pointId,
        string operation,
        string? rawResponse,
        string errorCode,
        string errorMessage)
    {
        Set(new ScpiCachedValue(
            sourcePath,
            endpointId,
            pointId,
            null,
            null,
            Contracts.Scpi.ScpiQuality.Bad,
            DateTimeOffset.UtcNow,
            operation,
            rawResponse,
            errorCode,
            errorMessage,
            false,
            null));
    }

    public void MarkStale(string sourcePath, string reason)
    {
        if (!_values.TryGetValue(sourcePath, out var existing))
        {
            return;
        }

        _values[sourcePath] = existing with
        {
            Stale = true,
            StaleReason = reason
        };
        Changed?.Invoke();
    }

    public ScpiCachedValue? Get(string sourcePath) =>
        _values.TryGetValue(sourcePath, out var value) ? value : null;

    public IReadOnlyList<ScpiCachedValue> Snapshot() =>
        _values.Values.OrderBy(value => value.SourcePath, StringComparer.OrdinalIgnoreCase).ToList();
}
