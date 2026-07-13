namespace Ptlk.RedisScpi.Models;

public sealed class SystemLogEntry
{
    public int Id { get; set; }
    public string Category { get; set; } = "System";
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
    public string? CommandId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
