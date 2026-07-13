using System.Text.Json;

namespace Ptlk.RedisScpi.Contracts.Scpi;

public sealed record ScpiConvertedValue(
    object Value,
    string RedisValue,
    JsonElement JsonValue,
    string ScpiValue);

public sealed record ScpiReadResult(
    string EndpointId,
    string PointId,
    string CommandText,
    string RawResponse,
    ScpiConvertedValue ConvertedValue,
    TimeSpan Duration,
    string? ErrorQueueResponse = null);

public sealed record ScpiWriteReadbackResult(
    string EndpointId,
    string PointId,
    string WriteCommandText,
    string ReadCommandText,
    string RawResponse,
    ScpiConvertedValue ExpectedValue,
    ScpiConvertedValue ActualValue,
    bool Matches,
    TimeSpan Duration,
    string? ErrorQueueResponse = null);

public sealed record ScpiDirectWriteResult(
    string EndpointId,
    string PointId,
    string CommandText,
    ScpiConvertedValue RequestedValue,
    TimeSpan Duration,
    string? ErrorQueueResponse = null);

public sealed record ScpiErrorQueueResult(
    bool Success,
    int Code,
    string Message,
    string RawResponse);
