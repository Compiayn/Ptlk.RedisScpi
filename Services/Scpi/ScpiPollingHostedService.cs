using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Models;
using Ptlk.RedisScpi.Services.Redis;
using Ptlk.RedisScpi.Services.Startup;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class ScpiPollingHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IScpiClientService client,
    EndpointOperationScheduler scheduler,
    ScpiValueCache cache,
    ScpiQualityPolicy qualityPolicy,
    RedisPointOwnershipService ownership,
    RedisPointStateService pointState,
    RuntimeModeService runtime,
    IOptions<RedisScpiOptions> redisScpiOptions,
    ILogger<ScpiPollingHostedService> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _nextDue = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var configuration = await LoadConfigurationAsync(stoppingToken);
                await PollDuePointsAsync(configuration, stoppingToken);
                await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                runtime.SetPolling(RuntimeSubsystemStatus.Degraded, $"Polling loop failed: {ex.Message}");
                runtime.ReportRuntimeDiagnostic("polling", "loop", "failed", ex.Message);
                logger.LogWarning(ex, "SCPI polling loop failed; retrying.");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task<PollingConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var converterId = redisScpiOptions.Value.ConverterId;
        var endpoints = await db.ScpiEndpointConfigs.AsNoTracking()
            .AsSplitQuery()
            .Where(endpoint => endpoint.Enabled && endpoint.ConverterId == converterId)
            .Include(endpoint => endpoint.Points.Where(point => point.Enabled && point.PollingEnabled))
            .ThenInclude(point => point.EnumOptions)
            .OrderBy(endpoint => endpoint.EndpointId)
            .ToListAsync(cancellationToken);
        var sourcePaths = endpoints.SelectMany(endpoint => endpoint.Points).Select(point => point.SourcePath).ToList();
        var mappings = await db.RedisMappings.AsNoTracking()
            .Where(mapping => sourcePaths.Contains(mapping.SourcePath))
            .ToDictionaryAsync(mapping => mapping.SourcePath, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var active = sourcePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stalePath in _nextDue.Keys.Where(path => !active.Contains(path)).ToList())
        {
            _nextDue.Remove(stalePath);
        }

        runtime.SetEndpoint(
            RuntimeSubsystemStatus.Normal,
            endpoints.Count == 0
                ? $"No enabled SCPI endpoint is assigned to converter '{converterId}'."
                : $"{endpoints.Count} enabled SCPI endpoint(s), {sourcePaths.Count} polling point(s)." );
        return new PollingConfiguration(endpoints, mappings);
    }

    private async Task PollDuePointsAsync(PollingConfiguration configuration, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var endpoint in configuration.Endpoints)
        {
            var endpointFailure = false;
            foreach (var point in endpoint.Points.OrderBy(item => item.PointId))
            {
                var interval = TimeSpan.FromMilliseconds(point.PollingIntervalMs ?? endpoint.PollingIntervalMs);
                if (!_nextDue.TryGetValue(point.SourcePath, out var due))
                {
                    due = point.InitialRead ? DateTimeOffset.MinValue : now.Add(interval);
                    _nextDue[point.SourcePath] = due;
                }
                if (now < due)
                {
                    continue;
                }
                _nextDue[point.SourcePath] = now.Add(interval);

                if (endpointFailure)
                {
                    continue;
                }

                if (!configuration.Mappings.TryGetValue(point.SourcePath, out var mapping))
                {
                    cache.MarkStale(point.SourcePath, "A RedisMapping is required before polling.");
                    runtime.ReportRuntimeDiagnostic(
                        "mapping",
                        point.SourcePath,
                        "missing_mapping",
                        $"Polling is waiting for a RedisMapping for '{point.SourcePath}'.");
                    continue;
                }
                runtime.ClearRuntimeDiagnostic("mapping", point.SourcePath);

                if (!runtime.IsRedisOutputReady)
                {
                    cache.MarkStale(point.SourcePath, "Redis output gate is not ready.");
                    continue;
                }

                if (!await ownership.EnsureOwnedAsync(point.SourcePath, mapping.RedisKey, cancellationToken))
                {
                    cache.MarkStale(point.SourcePath, "Ownership is not held by this RedisScpi instance.");
                    runtime.ReportRuntimeDiagnostic(
                        "ownership",
                        point.SourcePath,
                        ScpiErrorCodes.OwnershipNotAcquired,
                        "Polling did not access the device because point ownership is not held.");
                    continue;
                }
                runtime.ClearRuntimeDiagnostic("ownership", point.SourcePath);

                endpointFailure = await scheduler.RunAsync(
                    endpoint.EndpointId,
                    token => PollPointWithinEndpointLockAsync(endpoint, point, mapping, configuration.Mappings, token),
                    cancellationToken);
            }
        }
    }

    private async Task<bool> PollPointWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        RedisMapping mapping,
        IReadOnlyDictionary<string, RedisMapping> mappings,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentClaim = await ownership.ClaimAsync(
                mapping.SourcePath,
                mapping.RedisKey,
                cancellationToken);
            if (!currentClaim.Acquired)
            {
                cache.MarkStale(point.SourcePath, "Ownership changed before the scheduled poll could start.");
                runtime.ReportRuntimeDiagnostic(
                    "ownership",
                    point.SourcePath,
                    ScpiErrorCodes.OwnershipNotAcquired,
                    "Polling did not access the device because ownership changed while waiting for the endpoint lock.");
                return false;
            }
            runtime.ClearRuntimeDiagnostic("ownership", point.SourcePath);

            var result = await client.ReadWithinEndpointLockAsync(endpoint, point, cancellationToken);
            try
            {
                await pointState.UpdateDynamicFieldsAsync(
                    mapping,
                    result.ConvertedValue.JsonValue,
                    ScpiQuality.Good,
                    redisScpiOptions.Value.SourceName,
                    cancellationToken);
                cache.SetGood(
                    point.SourcePath,
                    endpoint.EndpointId,
                    point.PointId,
                    result.ConvertedValue.JsonValue,
                    result.ConvertedValue.RedisValue,
                    "poll",
                    FormatResponses(result.RawResponse, result.ErrorQueueResponse));
                runtime.ClearRuntimeDiagnostic("polling", point.SourcePath);
                runtime.ClearRuntimeDiagnostic("transport", endpoint.EndpointId);
                runtime.ClearRedisOutputDiagnosticsForMapping(mapping.SourcePath, mapping.RedisKey);
                RestoreHealthySubsystemStatus();
            }
            catch (RedisPointStateException ex)
            {
                if (IsOwnershipFailure(ex))
                {
                    cache.MarkStale(point.SourcePath, "Ownership was lost before the Redis state update.");
                }
                runtime.ReportRedisOutputDiagnostic(
                    "polling",
                    mapping.SourcePath,
                    mapping.RedisKey,
                    ex.Status,
                    ex.Message);
            }
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = qualityPolicy.Classify(ex);
            if (failure.EndpointWide)
            {
                await MarkEndpointBadAsync(endpoint, mappings, failure, ex, cancellationToken);
                return true;
            }

            await MarkPointBadAsync(endpoint, point, mapping, failure, ex, cancellationToken);
            return false;
        }
    }

    private async Task MarkEndpointBadAsync(
        ScpiEndpointConfig endpoint,
        IReadOnlyDictionary<string, RedisMapping> mappings,
        ScpiFailureClassification failure,
        Exception exception,
        CancellationToken cancellationToken)
    {
        runtime.SetTransport(RuntimeSubsystemStatus.Degraded, exception.Message);
        runtime.SetPolling(RuntimeSubsystemStatus.Degraded, $"Endpoint '{endpoint.EndpointId}' polling failed.");
        runtime.ReportRuntimeDiagnostic("transport", endpoint.EndpointId, failure.ErrorCode, exception.Message);
        foreach (var point in endpoint.Points.Where(point => point.Enabled && point.PollingEnabled))
        {
            if (!mappings.TryGetValue(point.SourcePath, out var mapping) || !ownership.IsOwned(point.SourcePath))
            {
                cache.MarkStale(point.SourcePath, "Endpoint failed and ownership is not held.");
                continue;
            }

            await MarkPointBadAsync(endpoint, point, mapping, failure, exception, cancellationToken);
            var interval = TimeSpan.FromMilliseconds(point.PollingIntervalMs ?? endpoint.PollingIntervalMs);
            _nextDue[point.SourcePath] = DateTimeOffset.UtcNow.Add(interval);
        }
    }

    private async Task MarkPointBadAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        RedisMapping mapping,
        ScpiFailureClassification failure,
        Exception exception,
        CancellationToken cancellationToken)
    {
        runtime.SetPolling(RuntimeSubsystemStatus.Degraded, $"Polling failed for '{point.SourcePath}'.");
        runtime.ReportRuntimeDiagnostic("polling", point.SourcePath, failure.ErrorCode, exception.Message);
        var rawContext = RawContext(exception);
        try
        {
            await pointState.UpdateDynamicFieldsAsync(
                mapping,
                null,
                ScpiQuality.Bad,
                redisScpiOptions.Value.SourceName,
                cancellationToken);
            cache.SetBad(
                point.SourcePath,
                endpoint.EndpointId,
                point.PointId,
                "poll",
                rawContext,
                failure.ErrorCode,
                exception.Message);
        }
        catch (RedisPointStateException redisException)
        {
            if (IsOwnershipFailure(redisException))
            {
                cache.MarkStale(point.SourcePath, "Ownership was lost before the failed-quality update.");
            }
            runtime.ReportRedisOutputDiagnostic(
                "polling_failure",
                mapping.SourcePath,
                mapping.RedisKey,
                redisException.Status,
                redisException.Message);
        }
    }

    private void RestoreHealthySubsystemStatus()
    {
        var diagnostics = runtime.Current.RuntimeDiagnostics;
        if (!diagnostics.Any(item => item.Subsystem.Equals("transport", StringComparison.OrdinalIgnoreCase)))
        {
            runtime.SetTransport(RuntimeSubsystemStatus.Normal, "SCPI transport is operating normally.");
        }
        if (!diagnostics.Any(item => item.Subsystem.Equals("polling", StringComparison.OrdinalIgnoreCase)))
        {
            runtime.SetPolling(RuntimeSubsystemStatus.Normal, "SCPI polling is operating normally.");
        }
    }

    private static bool IsOwnershipFailure(RedisPointStateException exception) =>
        exception.Reason.Equals(ScpiErrorCodes.OwnershipNotAcquired, StringComparison.OrdinalIgnoreCase)
        || exception.Reason.Equals("ownership_missing", StringComparison.OrdinalIgnoreCase)
        || exception.Reason.Equals("owned_by_other", StringComparison.OrdinalIgnoreCase);

    private static string? RawContext(Exception exception) =>
        exception switch
        {
            ScpiOperationException operation => FormatResponses(operation.PointResponse, operation.ErrorQueueResponse),
            ScpiWriteOperationException write => FormatResponses(write.PointResponse, write.ErrorQueueResponse),
            ScpiInstrumentException instrument => $"error_queue: {instrument.RawResponse}",
            ScpiParseException parse when parse.RawResponse is not null => $"point: {parse.RawResponse}",
            _ => null
        };

    private static string? FormatResponses(string? pointResponse, string? errorQueueResponse)
    {
        if (pointResponse is null) return errorQueueResponse is null ? null : $"error_queue: {errorQueueResponse}";
        if (errorQueueResponse is null) return $"point: {pointResponse}";
        return $"point: {pointResponse}\nerror_queue: {errorQueueResponse}";
    }

    private sealed record PollingConfiguration(
        IReadOnlyList<ScpiEndpointConfig> Endpoints,
        IReadOnlyDictionary<string, RedisMapping> Mappings);
}
