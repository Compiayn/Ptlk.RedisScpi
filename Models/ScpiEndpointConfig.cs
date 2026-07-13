namespace Ptlk.RedisScpi.Models;

public sealed class ScpiEndpointConfig
{
    public int Id { get; set; }
    public string EndpointId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "tcp";
    public string? TcpHost { get; set; }
    public int? TcpPort { get; set; } = 5025;
    public int TimeoutMs { get; set; } = 3000;
    public int PollingIntervalMs { get; set; } = 1000;
    public string ConverterId { get; set; } = "";
    public string ErrorCheckMode { get; set; } = "after-write";
    public string ErrorQueueQuery { get; set; } = "SYSTem:ERRor?";
    public string CommandTerminator { get; set; } = "\n";
    public string ResponseTerminator { get; set; } = "\n";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public List<ScpiPointConfig> Points { get; set; } = [];
}
