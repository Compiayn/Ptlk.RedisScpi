namespace Ptlk.RedisScpi.Models;

public sealed class ScpiPointConfig
{
    public int Id { get; set; }
    public int EndpointConfigId { get; set; }
    public ScpiEndpointConfig? EndpointConfig { get; set; }
    public string PointId { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
    public string Access { get; set; } = "readonly";
    public string DataType { get; set; } = "string";
    public string? NumberType { get; set; }
    public string? StringFormat { get; set; } = "raw";
    public string? EnumFormat { get; set; }
    public string ReadTemplate { get; set; } = "{name}?";
    public string? WriteTemplate { get; set; }
    public string? Unit { get; set; }
    public bool PollingEnabled { get; set; }
    public int? PollingIntervalMs { get; set; }
    public bool InitialRead { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public List<ScpiEnumOption> EnumOptions { get; set; } = [];
}
