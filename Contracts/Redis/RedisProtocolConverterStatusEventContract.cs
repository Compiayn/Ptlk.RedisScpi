namespace Ptlk.RedisScpi.Contracts.Redis;

public sealed record RedisProtocolConverterStatusEventContract(
    int Schema,
    string Type,
    string MessageId,
    string ConverterId,
    string Status,
    long Timestamp,
    string Source,
    IReadOnlyDictionary<string, string?>? Metadata);
