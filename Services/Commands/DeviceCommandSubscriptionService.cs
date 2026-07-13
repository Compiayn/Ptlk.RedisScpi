using Ptlk.RedisScpi.Contracts.Redis;
using Ptlk.RedisScpi.Services.Redis;
using Ptlk.RedisScpi.Services.Startup;

namespace Ptlk.RedisScpi.Services.Commands;

public sealed class DeviceCommandSubscriptionService(
    IServiceScopeFactory scopeFactory,
    RuntimeModeService runtime,
    IRedisPubSubService pubSub,
    ILogger<DeviceCommandSubscriptionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        runtime.SetCommand(RuntimeSubsystemStatus.Starting, "Waiting to subscribe to Redis device-write commands.");
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!runtime.IsRedisOutputReady)
            {
                if (runtime.Current.RuntimeDiagnostics.Any(item =>
                        item.Subsystem.Equals("command", StringComparison.OrdinalIgnoreCase)))
                {
                    runtime.SetCommand(RuntimeSubsystemStatus.Degraded, "Command processing has active diagnostics while Redis output is unavailable.");
                }
                else
                {
                    runtime.SetCommand(RuntimeSubsystemStatus.Starting, "Waiting for Redis output startup requirements before subscribing to commands.");
                }
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            try
            {
                await pubSub.SubscribeAsync(RedisContractNames.DeviceWriteCommandChannel, DispatchAsync, stoppingToken);
                logger.LogInformation("Subscribed Redis channel {Channel}.", RedisContractNames.DeviceWriteCommandChannel);
                runtime.ClearRuntimeDiagnostic("command_subscription", RedisContractNames.DeviceWriteCommandChannel);
                if (!runtime.Current.RuntimeDiagnostics.Any(item =>
                        item.Subsystem.Equals("command", StringComparison.OrdinalIgnoreCase)))
                {
                    runtime.SetCommand(RuntimeSubsystemStatus.Normal, "Subscribed to Redis device-write commands.");
                }
                while (runtime.IsRedisOutputReady && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(500, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                runtime.SetCommand(RuntimeSubsystemStatus.Degraded, $"Redis command subscription failed: {ex.Message}");
                runtime.ReportRuntimeDiagnostic(
                    "command_subscription",
                    RedisContractNames.DeviceWriteCommandChannel,
                    "subscribe_failed",
                    ex.Message);
                logger.LogWarning(ex, "Redis command subscription failed; retrying.");
                await Task.Delay(2000, stoppingToken);
            }
            finally
            {
                await pubSub.UnsubscribeAsync(RedisContractNames.DeviceWriteCommandChannel);
            }
        }
    }

    private async Task DispatchAsync(string payload)
    {
        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcherService>();
        await dispatcher.DispatchRawAsync(payload);
    }
}
