using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Services.Logs;
using Ptlk.RedisScpi.Services.Startup;

namespace Ptlk.RedisScpi.Services.Commands;

public sealed class CommandExecutionCleanupHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<RedisScpiRuntimeOptions> options,
    ILogger<CommandExecutionCleanupHostedService> logger,
    RuntimeModeService? runtime = null,
    LogService? log = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Command execution retention cleanup failed.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-options.Value.CommandExecutionRetentionDays);
        var acceptedAge = TimeSpan.FromMilliseconds(Math.Max(options.Value.CommandDefaultTimeoutMs * 2L, 60_000));
        var staleAcceptedCutoff = now.Subtract(acceptedAge);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var staleAccepted = await db.CommandExecutions
            .FromSqlInterpolated(
                $"""
                SELECT * FROM command_executions
                WHERE status = 'accepted'
                  AND created_at < {staleAcceptedCutoff}
                ORDER BY created_at
                LIMIT 1000
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        runtime?.ClearRuntimeDiagnostics("command_accepted");
        foreach (var accepted in staleAccepted)
        {
            var message = $"Command '{accepted.CommandId}' for '{accepted.RedisKey}' has remained accepted since {accepted.CreatedAt:O}.";
            runtime?.ReportRuntimeDiagnostic(
                "command_accepted",
                accepted.CommandId,
                "stale_accepted",
                message);
            if (log is not null)
            {
                try
                {
                    await log.AddSystemAsync("Command", "Warning", message, accepted.CommandId, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to persist stale accepted command diagnostic.");
                }
            }
        }

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM command_executions
            WHERE status IN ('completed', 'failed')
              AND completed_at IS NOT NULL
              AND completed_at < {cutoff}
            """,
            cancellationToken);
    }
}
