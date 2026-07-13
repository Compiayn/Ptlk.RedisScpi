using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using StackExchange.Redis;

namespace Ptlk.RedisScpi.Services.Redis;

public sealed class RedisConnectionFactory(
    IOptions<RedisOptions> options,
    ILogger<RedisConnectionFactory> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _connection;
    private bool _disposed;

    public async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is { IsConnected: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is { IsConnected: true })
            {
                return _connection;
            }

            _connection?.Dispose();
            var configuration = CreateConfiguration(options.Value);
            logger.LogInformation(
                "Connecting Redis {Host}:{Port}, db {DatabaseIndex}",
                options.Value.Host,
                options.Value.Port,
                options.Value.DatabaseIndex);

            _connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return connection.GetDatabase(options.Value.DatabaseIndex);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var database = await GetDatabaseAsync(cancellationToken);
            _ = await database.PingAsync();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Redis connectivity check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static ConfigurationOptions CreateConfiguration(RedisOptions redis)
    {
        var configuration = new ConfigurationOptions
        {
            ConnectTimeout = redis.ConnectTimeoutMs,
            SyncTimeout = redis.SyncTimeoutMs,
            AbortOnConnectFail = redis.AbortConnect,
            ConnectRetry = redis.ConnectRetry,
            KeepAlive = redis.KeepAliveSeconds,
            Ssl = redis.Ssl
        };
        configuration.EndPoints.Add(redis.Host, redis.Port);
        if (!string.IsNullOrWhiteSpace(redis.AclUsername))
        {
            configuration.User = redis.AclUsername;
        }

        if (!string.IsNullOrWhiteSpace(redis.AclPassword))
        {
            configuration.Password = redis.AclPassword;
        }

        return configuration;
    }
}
