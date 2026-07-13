using System.Text.Json;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Contracts.Redis;
using Ptlk.RedisScpi.Services.Logs;
using Ptlk.RedisScpi.Services.Redis;

namespace Ptlk.RedisScpi.Services.Commands;

public sealed record CommandDispatchResult(string Status, string Message, string? CommandId);
public sealed record PublishedWriteCommand(string CommandId, string RedisKey);

public sealed class CommandDispatcherService(
    CommandExecutionService execution,
    IRedisPubSubService pubSub,
    LogService log,
    IOptions<RedisScpiOptions> options,
    ILogger<CommandDispatcherService> logger)
{
    public async Task<CommandDispatchResult> DispatchRawAsync(
        string payload,
        CancellationToken cancellationToken = default)
    {
        DeviceWriteCommandContract? command;
        string canonical;
        try
        {
            canonical = CanonicalJson.Normalize(payload);
            command = JsonSerializer.Deserialize<DeviceWriteCommandContract>(canonical, RedisContractJson.WebOptions);
        }
        catch (JsonException ex)
        {
            await SafeLogAsync("Warning", $"Invalid command JSON: {ex.Message}", null, cancellationToken);
            return new CommandDispatchResult("failed", "Invalid command JSON.", null);
        }

        if (command is null)
        {
            await SafeLogAsync("Warning", "Ignored empty command payload.", null, cancellationToken);
            return new CommandDispatchResult("failed", "Command payload is required.", null);
        }
        if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 160)
        {
            await SafeLogAsync("Warning", "Ignored command without a valid commandId.", null, cancellationToken);
            return new CommandDispatchResult("failed", "commandId is required.", null);
        }
        if (string.IsNullOrWhiteSpace(command.Key)
            || command.Key.Length > 320
            || !command.Key.StartsWith(RedisContractNames.PointPrefix, StringComparison.OrdinalIgnoreCase)
            || command.Key.Length == RedisContractNames.PointPrefix.Length
            || command.Key.Contains('*', StringComparison.Ordinal))
        {
            await SafeLogAsync(
                "Warning",
                $"Ignored command '{command.CommandId}' with invalid point key.",
                command.CommandId,
                cancellationToken);
            return new CommandDispatchResult("ignored", "A valid point: key is required.", command.CommandId);
        }

        var payloadValidationError = ValidatePayload(command);
        return await execution.AcceptAsync(command, canonical, payloadValidationError, cancellationToken);
    }

    public async Task<PublishedWriteCommand> PublishHmiWriteAsync(
        string redisKey,
        JsonElement value,
        long? expectedVersion,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        var command = DeviceWriteCommandContract.Create(
            redisKey,
            value,
            requestedBy,
            options.Value.SourceName,
            expectedVersion: expectedVersion);
        await pubSub.PublishAsync(RedisContractNames.DeviceWriteCommandChannel, command, cancellationToken);
        return new PublishedWriteCommand(command.CommandId, command.Key);
    }

    private static string? ValidatePayload(DeviceWriteCommandContract command)
    {
        if (command.Schema != 1) return "schema must be 1.";
        if (!string.Equals(command.Type, "command.write-requested", StringComparison.Ordinal))
        {
            return "type must be 'command.write-requested'.";
        }
        if (string.IsNullOrWhiteSpace(command.MessageId) || command.MessageId.Length > 160)
        {
            return "messageId is required and must not exceed 160 characters.";
        }
        if (string.IsNullOrWhiteSpace(command.RequestedBy) || command.RequestedBy.Length > 160)
        {
            return "requestedBy is required and must not exceed 160 characters.";
        }
        if (string.IsNullOrWhiteSpace(command.Source) || command.Source.Length > 160)
        {
            return "source is required and must not exceed 160 characters.";
        }
        if (command.Timestamp <= 0) return "timestamp must be a positive Unix millisecond value.";
        if (command.ExpectedVersion is < 0) return "expectedVersion must be zero or greater.";
        if (command.TimeoutMs is <= 0) return "timeoutMs must be greater than zero when provided.";
        if (command.Value.ValueKind is not (
                JsonValueKind.String
                or JsonValueKind.Number
                or JsonValueKind.True
                or JsonValueKind.False))
        {
            return "value must be a JSON scalar string, number, or boolean.";
        }
        if (command.Params is { } parameters
            && parameters.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
        {
            return "params must be a JSON object when provided.";
        }
        return null;
    }

    private async Task SafeLogAsync(
        string level,
        string message,
        string? commandId,
        CancellationToken cancellationToken)
    {
        try
        {
            await log.AddSystemAsync("Command", level, message, commandId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to persist command dispatcher diagnostic.");
        }
    }
}
