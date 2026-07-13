using System.Collections.Concurrent;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class EndpointOperationScheduler
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _endpointGates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> RunAsync<T>(
        string endpointId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var gate = _endpointGates.GetOrAdd(endpointId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task RunAsync(
        string endpointId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            endpointId,
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken);
}
