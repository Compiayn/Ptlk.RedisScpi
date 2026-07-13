using Microsoft.EntityFrameworkCore;
using Ptlk.RedisScpi.Contracts.Redis;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Services.Redis;
using Ptlk.RedisScpi.Services.Scpi;
using Ptlk.RedisScpi.Services.Startup;

namespace Ptlk.RedisScpi.Services.Browser;

public sealed record BrowserPointSnapshot(
    int PointConfigId,
    string EndpointId,
    string PointId,
    string DisplayName,
    string SourcePath,
    string Access,
    string DataType,
    string? Unit,
    bool EndpointEnabled,
    bool PointEnabled,
    string? RedisKey,
    ScpiCachedValue? LocalValue,
    PointStateContract? RedisState,
    string? RedisStateError,
    bool IsOwned,
    string OwnershipStatus,
    string? Owner);

public sealed record BrowserSnapshot(
    IReadOnlyList<BrowserPointSnapshot> Points,
    DateTimeOffset CapturedAt,
    RuntimeMode Mode,
    bool RedisConnected,
    bool AssetInitialized,
    bool RedisOutputReady,
    bool Truncated);

public sealed class BrowserSnapshotService : IDisposable
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(250);
    private const int MaximumPoints = 1000;
    private const int RedisReadParallelism = 12;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ScpiValueCache _cache;
    private readonly RedisPointStateService _pointState;
    private readonly RedisPointOwnershipService _ownership;
    private readonly RuntimeModeService _runtime;
    private readonly object _sync = new();
    private Timer? _timer;
    private bool _disposed;

    public BrowserSnapshotService(
        IDbContextFactory<AppDbContext> dbFactory,
        ScpiValueCache cache,
        RedisPointStateService pointState,
        RedisPointOwnershipService ownership,
        RuntimeModeService runtime)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _pointState = pointState;
        _ownership = ownership;
        _runtime = runtime;
        _cache.Changed += SignalChanged;
        _ownership.Changed += SignalChanged;
        _runtime.Changed += OnRuntimeChanged;
    }

    public event Action? SnapshotChanged;

    public async Task<BrowserSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var points = await db.ScpiPointConfigs.AsNoTracking()
            .Include(point => point.EndpointConfig)
            .OrderBy(point => point.EndpointConfig!.EndpointId)
            .ThenBy(point => point.PointId)
            .Take(MaximumPoints + 1)
            .ToListAsync(cancellationToken);
        var truncated = points.Count > MaximumPoints;
        if (truncated)
        {
            points.RemoveAt(points.Count - 1);
        }

        var sourcePaths = points.Select(point => point.SourcePath).ToList();
        var mappings = await db.RedisMappings.AsNoTracking()
            .Where(mapping => sourcePaths.Contains(mapping.SourcePath))
            .ToDictionaryAsync(mapping => mapping.SourcePath, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var claims = _ownership.Snapshot().ToDictionary(claim => claim.SourcePath, StringComparer.OrdinalIgnoreCase);
        var runtime = _runtime.Current;
        using var redisReadGate = new SemaphoreSlim(RedisReadParallelism, RedisReadParallelism);
        var itemTasks = points.Select(async point =>
        {
            mappings.TryGetValue(point.SourcePath, out var mapping);
            claims.TryGetValue(point.SourcePath, out var claim);
            PointStateContract? redisState = null;
            string? redisStateError = null;
            if (mapping is not null && runtime.RedisConnected)
            {
                await redisReadGate.WaitAsync(cancellationToken);
                try
                {
                    redisState = await _pointState.ReadAsync(mapping.RedisKey, cancellationToken);
                    if (redisState is null) redisStateError = "Redis key is missing.";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    redisStateError = ex.Message;
                }
                finally
                {
                    redisReadGate.Release();
                }
            }

            return new BrowserPointSnapshot(
                point.Id,
                point.EndpointConfig?.EndpointId ?? "-",
                point.PointId,
                point.DisplayName ?? point.Name,
                point.SourcePath,
                point.Access,
                point.DataType,
                point.Unit,
                point.EndpointConfig?.Enabled == true,
                point.Enabled,
                mapping?.RedisKey,
                _cache.Get(point.SourcePath),
                redisState,
                redisStateError,
                claim?.Acquired == true,
                claim?.Status ?? (mapping is null ? "missing_mapping" : "not_claimed"),
                claim?.Owner);
        });
        var items = await Task.WhenAll(itemTasks);

        var finalRuntime = _runtime.Current;
        return new BrowserSnapshot(
            items,
            DateTimeOffset.UtcNow,
            finalRuntime.Mode,
            finalRuntime.RedisConnected,
            finalRuntime.AssetInitialized,
            finalRuntime.RedisOutputReady,
            truncated);
    }

    private void OnRuntimeChanged(RuntimeState _) => SignalChanged();

    private void SignalChanged()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer ??= new Timer(_ => FlushChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(CoalesceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushChanged()
    {
        if (!_disposed) SnapshotChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _cache.Changed -= SignalChanged;
            _ownership.Changed -= SignalChanged;
            _runtime.Changed -= OnRuntimeChanged;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
