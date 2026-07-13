namespace Ptlk.RedisScpi.Services.Startup;

public enum RuntimeMode
{
    Starting,
    Normal,
    Degraded,
    Stopping
}

public enum RuntimeSubsystemStatus
{
    Starting,
    Normal,
    Degraded,
    Stopping
}

public sealed record RedisOutputDiagnostic(
    string SourcePath,
    string RedisKey,
    string Status,
    string Message,
    string Origin,
    DateTimeOffset UpdatedAt);

public sealed record RuntimeDiagnostic(
    string Subsystem,
    string Scope,
    string Status,
    string Message,
    DateTimeOffset UpdatedAt);

public sealed record RuntimeState(
    RuntimeMode Mode,
    bool RedisConnected,
    bool AssetInitialized,
    bool RedisOutputReady,
    string Message,
    DateTimeOffset UpdatedAt,
    RuntimeSubsystemStatus RedisOutputStatus,
    string RedisOutputMessage,
    RuntimeSubsystemStatus EndpointStatus,
    string EndpointMessage,
    RuntimeSubsystemStatus TransportStatus,
    string TransportMessage,
    RuntimeSubsystemStatus PollingStatus,
    string PollingMessage,
    RuntimeSubsystemStatus CommandStatus,
    string CommandMessage,
    RuntimeSubsystemStatus ScpiStatus,
    string ScpiMessage,
    IReadOnlyList<RedisOutputDiagnostic> RedisOutputDiagnostics,
    IReadOnlyList<RuntimeDiagnostic> RuntimeDiagnostics)
{
    public RuntimeSubsystemStatus AcquisitionStatus => PollingStatus;
    public string AcquisitionMessage => PollingMessage;
}

public sealed class RuntimeModeService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RedisOutputDiagnostic> _redisOutputDiagnostics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeDiagnostic> _runtimeDiagnostics = new(StringComparer.OrdinalIgnoreCase);
    private bool _redisConnected;
    private bool _assetInitialized;
    private RuntimeSubsystemStatus _redisOutputBaseStatus = RuntimeSubsystemStatus.Starting;
    private RuntimeSubsystemStatus _endpointStatus = RuntimeSubsystemStatus.Normal;
    private RuntimeSubsystemStatus _transportStatus = RuntimeSubsystemStatus.Normal;
    private RuntimeSubsystemStatus _pollingStatus = RuntimeSubsystemStatus.Normal;
    private RuntimeSubsystemStatus _commandStatus = RuntimeSubsystemStatus.Normal;
    private RuntimeSubsystemStatus _scpiStatus = RuntimeSubsystemStatus.Normal;
    private string _redisOutputBaseMessage = "Waiting for Redis and Asset initialization.";
    private string _endpointMessage = "No endpoint configuration failures have been reported.";
    private string _transportMessage = "No SCPI transport failures have been reported.";
    private string _pollingMessage = "No SCPI polling failures have been reported.";
    private string _commandMessage = "No command processing failures have been reported.";
    private string _scpiMessage = "No SCPI diagnostics have been reported.";
    private RuntimeState _current;

    public RuntimeModeService()
    {
        _current = BuildState(DateTimeOffset.UtcNow);
    }

    public event Action<RuntimeState>? Changed;

    public RuntimeState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool IsNormal => Current.Mode == RuntimeMode.Normal;
    public bool IsRedisConnected => Current.RedisConnected;
    public bool IsAssetInitialized => Current.AssetInitialized;
    public bool IsRedisOutputReady
    {
        get
        {
            return Current.RedisOutputReady;
        }
    }

    public void SetRedisOutput(
        RuntimeSubsystemStatus status,
        bool redisConnected,
        bool assetInitialized,
        string message)
    {
        Update(() =>
        {
            _redisOutputBaseStatus = status;
            _redisConnected = redisConnected;
            _assetInitialized = assetInitialized;
            _redisOutputBaseMessage = message;
        });
    }

    public void SetEndpoint(RuntimeSubsystemStatus status, string message) =>
        Update(() =>
        {
            _endpointStatus = status;
            _endpointMessage = message;
        });

    public void SetTransport(RuntimeSubsystemStatus status, string message) =>
        Update(() =>
        {
            _transportStatus = status;
            _transportMessage = message;
        });

    public void SetPolling(RuntimeSubsystemStatus status, string message) =>
        Update(() =>
        {
            _pollingStatus = status;
            _pollingMessage = message;
        });

    public void SetAcquisition(RuntimeSubsystemStatus status, string message) =>
        SetPolling(status, message);

    public void SetCommand(RuntimeSubsystemStatus status, string message) =>
        Update(() =>
        {
            _commandStatus = status;
            _commandMessage = message;
        });

    public void SetScpi(RuntimeSubsystemStatus status, string message) =>
        Update(() =>
        {
            _scpiStatus = status;
            _scpiMessage = message;
        });

    public void ReplaceRedisOutputDiagnostics(
        string origin,
        IEnumerable<RedisOutputDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Update(() =>
        {
            RemoveRedisDiagnostics(diagnostic => diagnostic.Origin.Equals(origin, StringComparison.OrdinalIgnoreCase));
            foreach (var diagnostic in diagnostics)
            {
                _redisOutputDiagnostics[RedisDiagnosticKey(
                    diagnostic.Origin,
                    diagnostic.SourcePath,
                    diagnostic.RedisKey)] = diagnostic;
            }
        });
    }

    public void ReportRedisOutputDiagnostic(
        string origin,
        string sourcePath,
        string redisKey,
        string status,
        string message)
    {
        Update(() =>
        {
            var diagnostic = new RedisOutputDiagnostic(
                sourcePath,
                redisKey,
                status,
                message,
                origin,
                DateTimeOffset.UtcNow);
            _redisOutputDiagnostics[RedisDiagnosticKey(origin, sourcePath, redisKey)] = diagnostic;
        });
    }

    public void ClearRedisOutputDiagnosticsForMapping(string sourcePath, string redisKey)
    {
        Update(() => RemoveRedisDiagnostics(diagnostic =>
            diagnostic.SourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase)
            && diagnostic.RedisKey.Equals(redisKey, StringComparison.OrdinalIgnoreCase)));
    }

    public void ClearRedisOutputDiagnostic(string origin, string sourcePath, string redisKey)
    {
        Update(() => _redisOutputDiagnostics.Remove(RedisDiagnosticKey(origin, sourcePath, redisKey)));
    }

    public void ReportRuntimeDiagnostic(
        string subsystem,
        string scope,
        string status,
        string message)
    {
        Update(() =>
        {
            var diagnostic = new RuntimeDiagnostic(
                subsystem,
                scope,
                status,
                message,
                DateTimeOffset.UtcNow);
            _runtimeDiagnostics[RuntimeDiagnosticKey(subsystem, scope)] = diagnostic;
        });
    }

    public void ClearRuntimeDiagnostic(string subsystem, string scope)
    {
        Update(() => _runtimeDiagnostics.Remove(RuntimeDiagnosticKey(subsystem, scope)));
    }

    public void ClearRuntimeDiagnostics(string subsystem)
    {
        Update(() =>
        {
            var keys = _runtimeDiagnostics
                .Where(item => item.Value.Subsystem.Equals(subsystem, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Key)
                .ToList();
            foreach (var key in keys)
            {
                _runtimeDiagnostics.Remove(key);
            }
        });
    }

    public void Stop(string message = "Stopping")
    {
        Update(() =>
        {
            _redisOutputBaseStatus = RuntimeSubsystemStatus.Stopping;
            _endpointStatus = RuntimeSubsystemStatus.Stopping;
            _transportStatus = RuntimeSubsystemStatus.Stopping;
            _pollingStatus = RuntimeSubsystemStatus.Stopping;
            _commandStatus = RuntimeSubsystemStatus.Stopping;
            _scpiStatus = RuntimeSubsystemStatus.Stopping;
            _redisOutputBaseMessage = message;
            _endpointMessage = message;
            _transportMessage = message;
            _pollingMessage = message;
            _commandMessage = message;
            _scpiMessage = message;
        });
    }

    private void Update(Action mutate)
    {
        RuntimeState updated;
        bool changed;
        lock (_sync)
        {
            mutate();
            updated = BuildState(DateTimeOffset.UtcNow);
            changed = !StateEquivalent(_current, updated);
            _current = updated;
        }

        if (changed)
        {
            Changed?.Invoke(updated);
        }
    }

    private RuntimeState BuildState(DateTimeOffset updatedAt)
    {
        var redisDiagnostics = _redisOutputDiagnostics.Values
            .OrderBy(diagnostic => diagnostic.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Origin, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var runtimeDiagnostics = _runtimeDiagnostics.Values
            .OrderBy(diagnostic => diagnostic.Subsystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.Scope, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var redisOutputStatus = redisDiagnostics.Count == 0
            ? _redisOutputBaseStatus
            : RuntimeSubsystemStatus.Degraded;
        var redisOutputMessage = redisDiagnostics.Count == 0
            ? _redisOutputBaseMessage
            : $"Redis output degraded: {redisDiagnostics[0].Message}";
        // Point-level diagnostics degrade status visibility, but do not close the
        // startup gate for every other healthy point or prevent recovery attempts.
        var redisOutputReady = _redisConnected
                               && _assetInitialized
                               && _redisOutputBaseStatus == RuntimeSubsystemStatus.Normal;
        var statuses = new[]
        {
            redisOutputStatus,
            _endpointStatus,
            _transportStatus,
            _pollingStatus,
            _commandStatus,
            _scpiStatus
        };
        var mode = DeriveMode(statuses);
        var message = BuildMessage(
            mode,
            (redisOutputStatus, redisOutputMessage),
            (_endpointStatus, _endpointMessage),
            (_transportStatus, _transportMessage),
            (_pollingStatus, _pollingMessage),
            (_commandStatus, _commandMessage),
            (_scpiStatus, _scpiMessage));

        return new RuntimeState(
            mode,
            _redisConnected,
            _assetInitialized,
            redisOutputReady,
            message,
            updatedAt,
            redisOutputStatus,
            redisOutputMessage,
            _endpointStatus,
            _endpointMessage,
            _transportStatus,
            _transportMessage,
            _pollingStatus,
            _pollingMessage,
            _commandStatus,
            _commandMessage,
            _scpiStatus,
            _scpiMessage,
            redisDiagnostics,
            runtimeDiagnostics);
    }

    private void RemoveRedisDiagnostics(Func<RedisOutputDiagnostic, bool> predicate)
    {
        var keys = _redisOutputDiagnostics
            .Where(item => predicate(item.Value))
            .Select(item => item.Key)
            .ToList();
        foreach (var key in keys)
        {
            _redisOutputDiagnostics.Remove(key);
        }
    }

    private static RuntimeMode DeriveMode(IEnumerable<RuntimeSubsystemStatus> statuses)
    {
        var snapshot = statuses.ToList();
        if (snapshot.Any(status => status == RuntimeSubsystemStatus.Stopping))
        {
            return RuntimeMode.Stopping;
        }

        if (snapshot.Any(status => status == RuntimeSubsystemStatus.Degraded))
        {
            return RuntimeMode.Degraded;
        }

        return snapshot.Any(status => status == RuntimeSubsystemStatus.Starting)
            ? RuntimeMode.Starting
            : RuntimeMode.Normal;
    }

    private static string BuildMessage(
        RuntimeMode mode,
        params (RuntimeSubsystemStatus Status, string Message)[] subsystems)
    {
        if (mode == RuntimeMode.Normal)
        {
            return "Redis output and SCPI runtime are normal.";
        }

        return string.Join(
            " ",
            subsystems
                .Where(subsystem => subsystem.Status != RuntimeSubsystemStatus.Normal)
                .Select(subsystem => subsystem.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private static bool StateEquivalent(RuntimeState left, RuntimeState right) =>
        left.Mode == right.Mode
        && left.RedisConnected == right.RedisConnected
        && left.AssetInitialized == right.AssetInitialized
        && left.RedisOutputReady == right.RedisOutputReady
        && left.Message.Equals(right.Message, StringComparison.Ordinal)
        && left.RedisOutputStatus == right.RedisOutputStatus
        && left.RedisOutputMessage.Equals(right.RedisOutputMessage, StringComparison.Ordinal)
        && left.EndpointStatus == right.EndpointStatus
        && left.EndpointMessage.Equals(right.EndpointMessage, StringComparison.Ordinal)
        && left.TransportStatus == right.TransportStatus
        && left.TransportMessage.Equals(right.TransportMessage, StringComparison.Ordinal)
        && left.PollingStatus == right.PollingStatus
        && left.PollingMessage.Equals(right.PollingMessage, StringComparison.Ordinal)
        && left.CommandStatus == right.CommandStatus
        && left.CommandMessage.Equals(right.CommandMessage, StringComparison.Ordinal)
        && left.ScpiStatus == right.ScpiStatus
        && left.ScpiMessage.Equals(right.ScpiMessage, StringComparison.Ordinal)
        && left.RedisOutputDiagnostics.SequenceEqual(right.RedisOutputDiagnostics)
        && left.RuntimeDiagnostics.SequenceEqual(right.RuntimeDiagnostics);

    private static string RedisDiagnosticKey(string origin, string sourcePath, string redisKey) =>
        $"{origin}|{sourcePath}|{redisKey}";

    private static string RuntimeDiagnosticKey(string subsystem, string scope) =>
        $"{subsystem}|{scope}";
}
