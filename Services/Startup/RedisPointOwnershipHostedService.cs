using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Services.Redis;

namespace Ptlk.RedisScpi.Services.Startup;

public sealed class RedisPointOwnershipHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    RedisPointOwnershipService ownership,
    RuntimeModeService runtime,
    IOptions<RedisScpiOptions> options,
    ILogger<RedisPointOwnershipHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var current = runtime.Current;
                if (!current.RedisConnected
                    || !current.AssetInitialized
                    || current.RedisOutputDiagnostics.Any(diagnostic =>
                        diagnostic.Origin.Equals("startup_gate", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(RefreshDelay, stoppingToken);
                    continue;
                }

                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var activeMappings = await (
                        from mapping in db.RedisMappings.AsNoTracking()
                        join point in db.ScpiPointConfigs.AsNoTracking()
                            on mapping.SourcePath equals point.SourcePath
                        where point.Enabled
                              && point.EndpointConfig != null
                              && point.EndpointConfig.Enabled
                              && point.EndpointConfig.ConverterId == options.Value.ConverterId
                        orderby mapping.SourcePath
                        select mapping)
                    .ToListAsync(stoppingToken);

                ownership.RetainClaims(activeMappings.Select(mapping => mapping.SourcePath));
                foreach (var mapping in activeMappings)
                {
                    await ownership.EnsureOwnedAsync(mapping.SourcePath, mapping.RedisKey, stoppingToken);
                }

                runtime.ClearRuntimeDiagnostic("ownership", "refresh");
                await Task.Delay(RefreshDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                runtime.ReportRuntimeDiagnostic(
                    "ownership",
                    "refresh",
                    "ownership_refresh_failed",
                    ex.Message);
                logger.LogWarning(ex, "RedisScpi ownership refresh failed; retrying.");
                await Task.Delay(RefreshDelay, stoppingToken);
            }
        }
    }
}
