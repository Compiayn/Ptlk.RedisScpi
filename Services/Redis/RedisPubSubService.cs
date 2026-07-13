using Ptlk.RedisScpi.Contracts.Redis;
using StackExchange.Redis;

namespace Ptlk.RedisScpi.Services.Redis;

public interface IRedisPubSubService
{
    Task PublishAsync(string channel, object payload, CancellationToken cancellationToken = default);
    Task PublishRawAsync(string channel, string payload, CancellationToken cancellationToken = default);
    Task<ChannelMessageQueue> SubscribeAsync(
        string channel,
        Func<string, Task> onMessage,
        CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string channel);
}

public sealed class RedisPubSubService(
    RedisConnectionFactory redis,
    ILogger<RedisPubSubService> logger) : IRedisPubSubService
{
    public Task PublishAsync(
        string channel,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return PublishRawAsync(channel, RedisContractJson.Serialize(payload), cancellationToken);
    }

    public async Task PublishRawAsync(
        string channel,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await redis.GetConnectionAsync(cancellationToken);
        var subscriber = connection.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), payload);
    }

    public async Task<ChannelMessageQueue> SubscribeAsync(
        string channel,
        Func<string, Task> onMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(onMessage);

        var connection = await redis.GetConnectionAsync(cancellationToken);
        var subscriber = connection.GetSubscriber();
        var queue = await subscriber.SubscribeAsync(RedisChannel.Literal(channel));
        queue.OnMessage(message =>
        {
            _ = DispatchAsync(message.Message.ToString(), onMessage, cancellationToken);
        });

        return queue;
    }

    public async Task UnsubscribeAsync(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        try
        {
            var connection = await redis.GetConnectionAsync();
            await connection.GetSubscriber().UnsubscribeAsync(RedisChannel.Literal(channel));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unsubscribe Redis channel {Channel}", channel);
        }
    }

    private async Task DispatchAsync(
        string payload,
        Func<string, Task> onMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onMessage(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle a subscribed Redis message");
        }
    }
}
