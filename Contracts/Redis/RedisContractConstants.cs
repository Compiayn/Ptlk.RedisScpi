using System.Text.Json;

namespace Ptlk.RedisScpi.Contracts.Redis;

public static class RedisContractNames
{
    public const string InitializedKey = ".initialized";
    public const string PointPrefix = "point:";
    public const string PointPattern = "point:*";
    public const string ValueUpdatedChannel = "evt:value-updated";
    public const string DeviceWriteCommandChannel = "cmd:device-write";
    public const string CommandResultChannel = "evt:command-result";
    public const string EdgeStatusChannel = "evt:edge-status";
}

public static class RedisContractJson
{
    public static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize<T>(T payload) =>
        JsonSerializer.Serialize(payload, WebOptions);
}
