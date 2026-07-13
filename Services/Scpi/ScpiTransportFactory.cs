using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class ScpiTransportFactory(
    IOptions<ScpiRuntimeOptions> runtimeOptions,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TransportEntry> _transports =
        new(StringComparer.OrdinalIgnoreCase);

    public IScpiTransport GetTransport(ScpiEndpointConfig endpoint)
    {
        if (!endpoint.Transport.Equals("tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScpiValidationException(
                ScpiErrorCodes.ConfigurationInvalid,
                $"Transport '{endpoint.Transport}' is not supported in this release.");
        }

        var signature = $"{endpoint.Transport}|{endpoint.TcpHost}|{endpoint.TcpPort}";
        while (true)
        {
            if (_transports.TryGetValue(endpoint.EndpointId, out var existing))
            {
                if (existing.Signature.Equals(signature, StringComparison.OrdinalIgnoreCase))
                {
                    return existing.Transport;
                }

                if (_transports.TryRemove(endpoint.EndpointId, out var removed))
                {
                    _ = removed.Transport.DisposeAsync();
                }

                continue;
            }

            var transport = new TcpScpiTransport(
                runtimeOptions,
                loggerFactory.CreateLogger<TcpScpiTransport>());
            if (_transports.TryAdd(endpoint.EndpointId, new TransportEntry(signature, transport)))
            {
                return transport;
            }

            _ = transport.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _transports.Values)
        {
            await entry.Transport.DisposeAsync();
        }

        _transports.Clear();
    }

    private sealed record TransportEntry(string Signature, IScpiTransport Transport);
}
