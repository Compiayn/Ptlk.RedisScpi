namespace Ptlk.RedisScpi.Models;

public sealed class ScpiEnumOption
{
    public int Id { get; set; }
    public int ScpiPointConfigId { get; set; }
    public ScpiPointConfig? ScpiPointConfig { get; set; }
    public string DisplayName { get; set; } = "";
    public string Value { get; set; } = "";
    public int Code { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
