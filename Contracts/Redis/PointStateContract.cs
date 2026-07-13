using System.Text.Json;

namespace Ptlk.RedisScpi.Contracts.Redis;

public sealed record PointStateContract(
    string Key,
    JsonElement? Value,
    string? ValueText,
    bool HasValueField,
    string Quality,
    string Type,
    long Timestamp,
    long Version,
    string Source,
    string Access,
    string Unit,
    string? Owner,
    string? OwnerSource,
    long? OwnerAcquiredAt);

public enum RedisPointInspectionStatus
{
    Missing,
    Complete,
    Incomplete,
    Invalid
}

public sealed record RedisPointInspection(
    string Key,
    RedisPointInspectionStatus Status,
    PointStateContract? State,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsComplete => Status == RedisPointInspectionStatus.Complete && State is not null;
}
