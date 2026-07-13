using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public interface IScpiTransport : IAsyncDisposable
{
    Task SendCommandAsync(
        ScpiEndpointConfig endpoint,
        string command,
        CancellationToken cancellationToken = default);

    Task<string> QueryAsync(
        ScpiEndpointConfig endpoint,
        string command,
        CancellationToken cancellationToken = default);
}
