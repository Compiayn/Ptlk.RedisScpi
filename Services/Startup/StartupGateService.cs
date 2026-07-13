using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Contracts.Redis;
using Ptlk.RedisScpi.Services.Redis;

namespace Ptlk.RedisScpi.Services.Startup;

public sealed class StartupGateService(
    IServiceScopeFactory scopeFactory,
    RedisConnectionFactory redis,
    RuntimeModeService runtime,
    IOptions<StartupGateOptions> options,
    ILogger<StartupGateService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(options.Value.WaitInitializedTimeoutMs);
        var delay = options.Value.InitialRetryDelayMs;
        runtime.SetRedisOutput(
            RuntimeSubsystemStatus.Starting,
            redisConnected: false,
            assetInitialized: false,
            "Waiting for Redis and Asset initialization.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var redisConnected = await redis.IsConnectedAsync(stoppingToken);
                var initialized = false;
                IReadOnlyList<RedisMappingRuntimeIssue> mappingIssues = [];

                if (redisConnected)
                {
                    var database = await redis.GetDatabaseAsync(stoppingToken);
                    initialized = (await database.StringGetAsync(RedisContractNames.InitializedKey)).ToString() == "1";
                    if (initialized)
                    {
                        using var scope = scopeFactory.CreateScope();
                        var validator = scope.ServiceProvider.GetRequiredService<RedisMappingValidationService>();
                        mappingIssues = await validator.VerifyRuntimeMappingsAsync(stoppingToken);
                    }
                }

                runtime.ReplaceRedisOutputDiagnostics(
                    "startup_gate",
                    mappingIssues.Select(issue => new RedisOutputDiagnostic(
                        issue.SourcePath,
                        issue.RedisKey,
                        issue.Status,
                        issue.Message,
                        "startup_gate",
                        DateTimeOffset.UtcNow)));

                if (redisConnected && initialized && mappingIssues.Count == 0)
                {
                    runtime.SetRedisOutput(
                        RuntimeSubsystemStatus.Normal,
                        redisConnected: true,
                        assetInitialized: true,
                        "Asset initialized and enabled SCPI Redis mappings are canonical.");
                    delay = options.Value.InitialRetryDelayMs;
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                var shouldDegrade = DateTimeOffset.UtcNow >= deadline
                                    || (redisConnected && initialized && mappingIssues.Count > 0);
                var message = shouldDegrade
                    ? BuildDegradedReason(redisConnected, initialized, mappingIssues)
                    : BuildWaitingReason(redisConnected, initialized);
                runtime.SetRedisOutput(
                    shouldDegrade ? RuntimeSubsystemStatus.Degraded : RuntimeSubsystemStatus.Starting,
                    redisConnected,
                    initialized,
                    message);

                if (shouldDegrade)
                {
                    logger.LogWarning("RedisScpi startup gate is degraded: {Reason}", message);
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                await Task.Delay(delay, stoppingToken);
                delay = Math.Min(delay * 2, options.Value.MaxRetryDelayMs);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RedisScpi startup gate check failed.");
                runtime.SetRedisOutput(
                    RuntimeSubsystemStatus.Degraded,
                    redisConnected: false,
                    assetInitialized: false,
                    $"Redis output degraded: startup gate check failed: {ex.Message}");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        runtime.Stop();
        return base.StopAsync(cancellationToken);
    }

    private static string BuildWaitingReason(bool redisConnected, bool initialized)
    {
        if (!redisConnected)
        {
            return "Waiting for Redis connection.";
        }

        return !initialized
            ? "Waiting for Asset .initialized = 1."
            : "Validating enabled SCPI Redis mappings.";
    }

    private static string BuildDegradedReason(
        bool redisConnected,
        bool initialized,
        IReadOnlyList<RedisMappingRuntimeIssue> mappingIssues)
    {
        if (!redisConnected)
        {
            return "Redis output degraded: Redis is not connected.";
        }

        if (!initialized)
        {
            return "Redis output degraded: Asset .initialized is not 1.";
        }

        if (mappingIssues.Count > 0)
        {
            return $"Redis output degraded: {mappingIssues[0].Message}";
        }

        return "Redis output degraded: startup requirements were not met.";
    }
}
