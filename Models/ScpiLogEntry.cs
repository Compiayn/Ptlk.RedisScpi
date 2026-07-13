namespace Ptlk.RedisScpi.Models;

public sealed class ScpiLogEntry
{
    public int Id { get; set; }
    public string? EndpointId { get; set; }
    public string? PointId { get; set; }
    public string Operation { get; set; } = "";
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
    public string? CommandText { get; set; }
    public string? ResponseText { get; set; }
    public string? Quality { get; set; }
    public string? CommandId { get; set; }
    public string? ErrorCode { get; set; }
    public int? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
