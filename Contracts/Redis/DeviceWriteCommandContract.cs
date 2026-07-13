using System.Text.Json;

namespace Ptlk.RedisScpi.Contracts.Redis;

public sealed class DeviceWriteCommandContract
{
    public int Schema { get; set; } = 1;
    public string Type { get; set; } = "command.write-requested";
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public string CommandId { get; set; } = "";
    public string Key { get; set; } = "";
    public JsonElement Value { get; set; }
    public long? ExpectedVersion { get; set; }
    public int? TimeoutMs { get; set; }
    public int? Priority { get; set; }
    public JsonElement? Params { get; set; }
    public string RequestedBy { get; set; } = "";
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string Source { get; set; } = "";

    public string ValueAsString() =>
        Value.ValueKind switch
        {
            JsonValueKind.String => Value.GetString() ?? "",
            JsonValueKind.Number => Value.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => Value.GetRawText()
        };

    public static DeviceWriteCommandContract Create<TValue>(
        string key,
        TValue value,
        string requestedBy,
        string source,
        string? commandId = null,
        long? expectedVersion = null,
        int? timeoutMs = null,
        int? priority = null,
        object? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return new DeviceWriteCommandContract
        {
            CommandId = commandId ?? Guid.NewGuid().ToString("N"),
            Key = key.Trim(),
            Value = JsonSerializer.SerializeToElement(value, RedisContractJson.WebOptions),
            ExpectedVersion = expectedVersion,
            TimeoutMs = timeoutMs,
            Priority = priority,
            Params = parameters is null
                ? null
                : JsonSerializer.SerializeToElement(parameters, RedisContractJson.WebOptions),
            RequestedBy = requestedBy.Trim(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Source = source.Trim()
        };
    }
}
