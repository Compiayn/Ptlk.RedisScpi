using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class TcpScpiTransport(
    IOptions<ScpiRuntimeOptions> runtimeOptions,
    ILogger<TcpScpiTransport> logger) : IScpiTransport
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<byte> _pending = [];
    private TcpClient? _client;
    private string? _connectedHost;
    private int? _connectedPort;
    private bool _disposed;

    public async Task SendCommandAsync(
        ScpiEndpointConfig endpoint,
        string command,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await ExecuteWithTimeoutAsync(
                endpoint,
                async token =>
                {
                    var stream = await GetStreamAsync(endpoint, token);
                    await WriteAsync(stream, command, endpoint.CommandTerminator, token);
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> QueryAsync(
        ScpiEndpointConfig endpoint,
        string command,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteWithTimeoutAsync(
                endpoint,
                async token =>
                {
                    var stream = await GetStreamAsync(endpoint, token);
                    await WriteAsync(stream, command, endpoint.CommandTerminator, token);
                    return await ReadResponseAsync(stream, endpoint.ResponseTerminator, token);
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync();
        try
        {
            ResetConnection();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<NetworkStream> GetStreamAsync(ScpiEndpointConfig endpoint, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(endpoint.TcpHost) || endpoint.TcpPort is not (>= 1 and <= 65535))
        {
            throw new ScpiValidationException(
                ScpiErrorCodes.ConfigurationInvalid,
                $"Endpoint '{endpoint.EndpointId}' has invalid TCP settings.");
        }

        if (_client is { Connected: true }
            && string.Equals(_connectedHost, endpoint.TcpHost, StringComparison.OrdinalIgnoreCase)
            && _connectedPort == endpoint.TcpPort)
        {
            return _client.GetStream();
        }

        ResetConnection();
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(endpoint.TcpHost, endpoint.TcpPort.Value, cancellationToken);
            _client = client;
            _connectedHost = endpoint.TcpHost;
            _connectedPort = endpoint.TcpPort;
            logger.LogInformation(
                "Connected SCPI endpoint {EndpointId} at {Host}:{Port}.",
                endpoint.EndpointId,
                endpoint.TcpHost,
                endpoint.TcpPort);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<T> ExecuteWithTimeoutAsync<T>(
        ScpiEndpointConfig endpoint,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : runtimeOptions.Value.DefaultTimeoutMs);
        try
        {
            return await operation(timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            ResetConnection();
            throw new ScpiTimeoutException(
                $"SCPI operation timed out for endpoint '{endpoint.EndpointId}'.",
                ex);
        }
        catch (ScpiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            ResetConnection();
            throw new ScpiTransportException(
                $"SCPI TCP operation failed for endpoint '{endpoint.EndpointId}': {ex.Message}",
                ex);
        }
    }

    private Task ExecuteWithTimeoutAsync(
        ScpiEndpointConfig endpoint,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        ExecuteWithTimeoutAsync(
            endpoint,
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken);

    private static async Task WriteAsync(
        NetworkStream stream,
        string command,
        string terminator,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(command + DecodeTerminator(terminator));
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<string> ReadResponseAsync(
        NetworkStream stream,
        string configuredTerminator,
        CancellationToken cancellationToken)
    {
        var terminator = Encoding.UTF8.GetBytes(DecodeTerminator(configuredTerminator));
        if (terminator.Length == 0)
        {
            throw new ScpiValidationException(
                ScpiErrorCodes.ConfigurationInvalid,
                "SCPI response terminator must not be empty.");
        }

        var buffer = new byte[4096];
        while (true)
        {
            var terminatorIndex = IndexOf(_pending, terminator);
            if (terminatorIndex >= 0)
            {
                if (terminatorIndex > runtimeOptions.Value.MaxResponseBytes)
                {
                    ResetConnection();
                    throw new ScpiParseException(
                        $"SCPI response exceeded {runtimeOptions.Value.MaxResponseBytes} bytes.");
                }

                var responseBytes = _pending.Take(terminatorIndex).ToArray();
                _pending.RemoveRange(0, terminatorIndex + terminator.Length);
                return Encoding.UTF8.GetString(responseBytes);
            }

            if (_pending.Count >= runtimeOptions.Value.MaxResponseBytes)
            {
                ResetConnection();
                throw new ScpiParseException(
                    $"SCPI response exceeded {runtimeOptions.Value.MaxResponseBytes} bytes.");
            }

            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                ResetConnection();
                throw new ScpiTransportException("SCPI endpoint closed the TCP connection before a complete response.");
            }

            _pending.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    internal static string DecodeTerminator(string value) =>
        value.Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);

    private static int IndexOf(List<byte> source, byte[] target)
    {
        for (var index = 0; index <= source.Count - target.Length; index++)
        {
            var matches = true;
            for (var targetIndex = 0; targetIndex < target.Length; targetIndex++)
            {
                if (source[index + targetIndex] == target[targetIndex])
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }

    private void ResetConnection()
    {
        _pending.Clear();
        try
        {
            _client?.Dispose();
        }
        catch
        {
        }

        _client = null;
        _connectedHost = null;
        _connectedPort = null;
    }
}
