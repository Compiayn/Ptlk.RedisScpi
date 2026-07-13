using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Contracts.Scpi;

namespace Ptlk.RedisScpi.Configuration;

public sealed class RedisOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string? AclUsername { get; set; }
    public string? AclPassword { get; set; }
    public int DatabaseIndex { get; set; }
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int SyncTimeoutMs { get; set; } = 3000;
    public bool AbortConnect { get; set; }
    public int ConnectRetry { get; set; } = 3;
    public int KeepAliveSeconds { get; set; } = 60;
    public bool Ssl { get; set; }
}

public sealed class RedisScpiOptions
{
    public string ConverterId { get; set; } = "redis-scpi-local-1";
    public string SourceName { get; set; } = "redis-scpi";
}

public sealed class StartupGateOptions
{
    public int WaitInitializedTimeoutMs { get; set; } = 60000;
    public int InitialRetryDelayMs { get; set; } = 250;
    public int MaxRetryDelayMs { get; set; } = 5000;
}

public sealed class RedisScpiRuntimeOptions
{
    public int HeartbeatIntervalMs { get; set; } = 10000;
    public int CommandDefaultTimeoutMs { get; set; } = 5000;
    public int CommandExecutionRetentionDays { get; set; } = 7;
}

public sealed class ScpiRuntimeOptions
{
    public int DefaultTimeoutMs { get; set; } = 3000;
    public int DefaultPollingIntervalMs { get; set; } = 1000;
    public string DefaultErrorCheckMode { get; set; } = ScpiErrorCheckModes.AfterWrite;
    public string DefaultCommandTerminator { get; set; } = "\n";
    public string DefaultResponseTerminator { get; set; } = "\n";
    public string DefaultErrorQueueQuery { get; set; } = "SYSTem:ERRor?";
    public int MaxResponseBytes { get; set; } = 65536;
}

public sealed class BrowserOptions
{
    public int RefreshIntervalMs { get; set; } = 1000;
}

public sealed class ImportExportOptions
{
    public long SingleCsvLimitBytes { get; set; } = 10 * 1024 * 1024;
    public long ZipFileLimitBytes { get; set; } = 50 * 1024 * 1024;
    public long ZipExtractedLimitBytes { get; set; } = 50 * 1024 * 1024;
}

public static class OptionsRegistration
{
    public static IServiceCollection AddRedisScpiOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection("Redis"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host)
                           && o.Port is > 0 and <= 65535
                           && o.DatabaseIndex >= 0
                           && o.ConnectTimeoutMs > 0
                           && o.SyncTimeoutMs > 0
                           && o.ConnectRetry >= 0
                           && o.KeepAliveSeconds > 0,
                "Redis options are invalid.")
            .ValidateOnStart();

        services.AddOptions<RedisScpiOptions>()
            .Bind(configuration.GetSection("RedisScpi"))
            .Validate(o => IsSafeToken(o.ConverterId) && IsSafeToken(o.SourceName),
                "RedisScpi identifiers must be safe non-empty tokens.")
            .ValidateOnStart();

        services.AddOptions<StartupGateOptions>()
            .Bind(configuration.GetSection("StartupGate"))
            .Validate(o => o.WaitInitializedTimeoutMs > 0
                           && o.InitialRetryDelayMs > 0
                           && o.MaxRetryDelayMs >= o.InitialRetryDelayMs,
                "StartupGate retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<RedisScpiRuntimeOptions>()
            .Bind(configuration.GetSection("RedisScpiRuntime"))
            .Validate(o => o.HeartbeatIntervalMs > 0
                           && o.CommandDefaultTimeoutMs > 0
                           && o.CommandExecutionRetentionDays > 0,
                "RedisScpiRuntime options are invalid.")
            .ValidateOnStart();

        services.AddOptions<ScpiRuntimeOptions>()
            .Bind(configuration.GetSection("ScpiRuntime"))
            .Validate(o => o.DefaultTimeoutMs > 0
                           && o.DefaultPollingIntervalMs >= 100
                           && ScpiErrorCheckModes.IsValid(o.DefaultErrorCheckMode)
                           && !string.IsNullOrEmpty(o.DefaultCommandTerminator)
                           && !string.IsNullOrEmpty(o.DefaultResponseTerminator)
                           && !string.IsNullOrWhiteSpace(o.DefaultErrorQueueQuery)
                           && o.MaxResponseBytes is >= 1 and <= 4 * 1024 * 1024,
                "SCPI runtime options are invalid.")
            .ValidateOnStart();

        services.AddOptions<BrowserOptions>()
            .Bind(configuration.GetSection("Browser"))
            .Validate(o => o.RefreshIntervalMs is >= 100 and <= 60000,
                "Browser refresh interval is invalid.")
            .ValidateOnStart();

        services.AddOptions<ImportExportOptions>()
            .Bind(configuration.GetSection("ImportExport"))
            .Validate(o => o.SingleCsvLimitBytes > 0
                           && o.ZipFileLimitBytes > 0
                           && o.ZipExtractedLimitBytes >= o.SingleCsvLimitBytes,
                "Import/export file limits are invalid.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsSafeToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains(':', StringComparison.Ordinal)
        && !value.Contains('*', StringComparison.Ordinal)
        && !value.Contains(' ', StringComparison.Ordinal)
        && !value.Contains('/', StringComparison.Ordinal)
        && !value.Contains('\\', StringComparison.Ordinal);
}
