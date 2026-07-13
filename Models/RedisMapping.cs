namespace Ptlk.RedisScpi.Models;

public sealed class RedisMapping
{
    public int Id { get; set; }
    public string SourcePath { get; set; } = "";
    public string RedisKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
