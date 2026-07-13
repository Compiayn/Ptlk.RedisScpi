namespace Ptlk.RedisScpi.Models;

public sealed class CommandExecution
{
    public int Id { get; set; }
    public string CommandId { get; set; } = "";
    public string RedisKey { get; set; } = "";
    public string Status { get; set; } = "accepted";
    public string RequestedPayloadJson { get; set; } = "";
    public string? ResultPayloadJson { get; set; }
    public string? ActualValueJson { get; set; }
    public long? Version { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
