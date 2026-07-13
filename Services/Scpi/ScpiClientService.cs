using System.Diagnostics;
using System.Text.Json;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;
using Ptlk.RedisScpi.Services.Logs;

namespace Ptlk.RedisScpi.Services.Scpi;

public interface IScpiClientService
{
    Task<ScpiReadResult> ReadAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        CancellationToken cancellationToken = default);

    // The caller must already hold EndpointOperationScheduler for endpoint.EndpointId.
    Task<ScpiReadResult> ReadWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        CancellationToken cancellationToken = default);

    Task<ScpiWriteReadbackResult> WriteAndReadbackAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        string? commandId = null,
        CancellationToken cancellationToken = default);

    // The caller must already hold EndpointOperationScheduler for endpoint.EndpointId.
    Task<ScpiWriteReadbackResult> WriteAndReadbackWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        string? commandId = null,
        CancellationToken cancellationToken = default);

    Task<ScpiDirectWriteResult> DirectWriteAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        CancellationToken cancellationToken = default);

    // The caller must already hold EndpointOperationScheduler for endpoint.EndpointId.
    Task<ScpiDirectWriteResult> DirectWriteWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        CancellationToken cancellationToken = default);
}

public sealed class ScpiClientService(
    ScpiTransportFactory transportFactory,
    EndpointOperationScheduler scheduler,
    ScpiTemplateRenderer renderer,
    ScpiValueConverter converter,
    ScpiErrorQueueService errorQueue,
    LogService log,
    ILogger<ScpiClientService> logger) : IScpiClientService
{
    public Task<ScpiReadResult> ReadAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        CancellationToken cancellationToken = default) =>
        scheduler.RunAsync(
            endpoint.EndpointId,
            token => ReadWithinEndpointLockAsync(endpoint, point, token),
            cancellationToken);

    public async Task<ScpiReadResult> ReadWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(endpoint, point);
        var command = renderer.RenderRead(point);
        var stopwatch = Stopwatch.StartNew();
        string? pointResponse = null;
        string? errorQueueResponse = null;
        try
        {
            var transport = transportFactory.GetTransport(endpoint);
            pointResponse = await transport.QueryAsync(endpoint, command, cancellationToken);
            if (ScpiErrorCheckModes.AfterCommand.Equals(endpoint.ErrorCheckMode, StringComparison.OrdinalIgnoreCase))
            {
                var instrument = await errorQueue.QueryAsync(transport, endpoint, cancellationToken);
                errorQueueResponse = instrument.RawResponse;
                ScpiErrorQueueService.ThrowIfError(instrument);
            }

            var converted = converter.ParseResponse(point, pointResponse);
            var result = new ScpiReadResult(
                endpoint.EndpointId,
                point.PointId,
                command,
                pointResponse,
                converted,
                stopwatch.Elapsed,
                errorQueueResponse);

            await SafeLogAsync(
                endpoint,
                point,
                "poll",
                "Info",
                "SCPI query completed.",
                command,
                FormatResponses(pointResponse, errorQueueResponse),
                ScpiQuality.Good,
                null,
                null,
                stopwatch,
                cancellationToken);
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = WithRawContext(ex, pointResponse, errorQueueResponse);
            await SafeLogExceptionAsync(
                endpoint,
                point,
                "poll",
                command,
                FormatResponses(wrapped.PointResponse, wrapped.ErrorQueueResponse),
                wrapped,
                null,
                stopwatch,
                cancellationToken);
            throw wrapped;
        }
    }

    public Task<ScpiWriteReadbackResult> WriteAndReadbackAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        string? commandId = null,
        CancellationToken cancellationToken = default) =>
        scheduler.RunAsync(
            endpoint.EndpointId,
            token => WriteAndReadbackWithinEndpointLockAsync(endpoint, point, requestedValue, commandId, token),
            cancellationToken);

    public async Task<ScpiWriteReadbackResult> WriteAndReadbackWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        string? commandId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(endpoint, point);
        if (!ScpiAccessModes.Readwrite.Equals(point.Access, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScpiValidationException(ScpiErrorCodes.PointReadonly, $"Point '{point.SourcePath}' is readonly.");
        }

        var expected = converter.ConvertInput(point, requestedValue);
        var writeCommand = renderer.RenderWrite(point, expected.ScpiValue);
        var readCommand = renderer.RenderRead(point);
        var stopwatch = Stopwatch.StartNew();
        var commandWasSent = false;
        string? pointResponse = null;
        var errorQueueResponses = new List<string>();
        try
        {
            var transport = transportFactory.GetTransport(endpoint);
            await transport.SendCommandAsync(endpoint, writeCommand, cancellationToken);
            commandWasSent = true;

            if (ShouldCheckAfterWrite(endpoint))
            {
                var instrument = await errorQueue.QueryAsync(transport, endpoint, cancellationToken);
                errorQueueResponses.Add(instrument.RawResponse);
                ScpiErrorQueueService.ThrowIfError(instrument);
            }

            pointResponse = await transport.QueryAsync(endpoint, readCommand, cancellationToken);
            if (ScpiErrorCheckModes.AfterCommand.Equals(endpoint.ErrorCheckMode, StringComparison.OrdinalIgnoreCase))
            {
                var instrument = await errorQueue.QueryAsync(transport, endpoint, cancellationToken);
                errorQueueResponses.Add(instrument.RawResponse);
                ScpiErrorQueueService.ThrowIfError(instrument);
            }

            var actual = converter.ParseResponse(point, pointResponse);
            var combinedErrorQueue = CombineErrorQueueResponses(errorQueueResponses);
            var result = new ScpiWriteReadbackResult(
                endpoint.EndpointId,
                point.PointId,
                writeCommand,
                readCommand,
                pointResponse,
                expected,
                actual,
                converter.AreEqual(expected, actual),
                stopwatch.Elapsed,
                combinedErrorQueue);

            await SafeLogAsync(
                endpoint,
                point,
                "verify",
                result.Matches ? "Info" : "Warning",
                result.Matches ? "SCPI write readback matched." : "SCPI write readback did not match.",
                writeCommand,
                FormatResponses(pointResponse, combinedErrorQueue),
                result.Matches ? ScpiQuality.Good : ScpiQuality.Bad,
                commandId,
                result.Matches ? null : ScpiErrorCodes.WriteVerificationFailed,
                stopwatch,
                cancellationToken);
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = WithRawContext(ex, pointResponse, CombineErrorQueueResponses(errorQueueResponses));
            await SafeLogExceptionAsync(
                endpoint,
                point,
                "write",
                writeCommand,
                FormatResponses(wrapped.PointResponse, wrapped.ErrorQueueResponse),
                wrapped,
                commandId,
                stopwatch,
                cancellationToken);
            throw new ScpiWriteOperationException(
                wrapped.ErrorCode,
                wrapped.Message,
                commandWasSent,
                wrapped,
                wrapped.PointResponse,
                wrapped.ErrorQueueResponse);
        }
    }

    public Task<ScpiDirectWriteResult> DirectWriteAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        CancellationToken cancellationToken = default) =>
        scheduler.RunAsync(
            endpoint.EndpointId,
            token => DirectWriteWithinEndpointLockAsync(endpoint, point, requestedValue, token),
            cancellationToken);

    public async Task<ScpiDirectWriteResult> DirectWriteWithinEndpointLockAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        JsonElement requestedValue,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable(endpoint, point);
        if (!ScpiAccessModes.Readwrite.Equals(point.Access, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScpiValidationException(ScpiErrorCodes.PointReadonly, $"Point '{point.SourcePath}' is readonly.");
        }

        var requested = converter.ConvertInput(point, requestedValue);
        var command = renderer.RenderWrite(point, requested.ScpiValue);
        var stopwatch = Stopwatch.StartNew();
        string? errorQueueResponse = null;
        try
        {
            var transport = transportFactory.GetTransport(endpoint);
            await transport.SendCommandAsync(endpoint, command, cancellationToken);
            if (ShouldCheckAfterWrite(endpoint))
            {
                var instrument = await errorQueue.QueryAsync(transport, endpoint, cancellationToken);
                errorQueueResponse = instrument.RawResponse;
                ScpiErrorQueueService.ThrowIfError(instrument);
            }

            var result = new ScpiDirectWriteResult(
                endpoint.EndpointId,
                point.PointId,
                command,
                requested,
                stopwatch.Elapsed,
                errorQueueResponse);

            await SafeLogAsync(
                endpoint,
                point,
                "direct_write",
                "Info",
                "Direct SCPI diagnostic write completed; runtime value awaits normal polling.",
                command,
                FormatResponses(null, result.ErrorQueueResponse),
                null,
                null,
                null,
                stopwatch,
                cancellationToken);
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = WithRawContext(ex, null, errorQueueResponse);
            await SafeLogExceptionAsync(
                endpoint,
                point,
                "direct_write",
                command,
                FormatResponses(wrapped.PointResponse, wrapped.ErrorQueueResponse),
                wrapped,
                null,
                stopwatch,
                cancellationToken);
            throw wrapped;
        }
    }

    private static void EnsureAvailable(ScpiEndpointConfig endpoint, ScpiPointConfig point)
    {
        if (!endpoint.Enabled)
        {
            throw new ScpiValidationException(
                ScpiErrorCodes.EndpointDisabled,
                $"Endpoint '{endpoint.EndpointId}' is disabled.");
        }

        if (!point.Enabled)
        {
            throw new ScpiValidationException(
                ScpiErrorCodes.PointDisabled,
                $"Point '{point.SourcePath}' is disabled.");
        }
    }

    private static bool ShouldCheckAfterWrite(ScpiEndpointConfig endpoint) =>
        ScpiErrorCheckModes.AfterWrite.Equals(endpoint.ErrorCheckMode, StringComparison.OrdinalIgnoreCase)
        || ScpiErrorCheckModes.AfterCommand.Equals(endpoint.ErrorCheckMode, StringComparison.OrdinalIgnoreCase);

    private static ScpiOperationException WithRawContext(
        Exception exception,
        string? pointResponse,
        string? errorQueueResponse)
    {
        var operation = exception as ScpiOperationException;
        var code = exception is ScpiException scpi ? scpi.ErrorCode : ScpiErrorCodes.TransportError;
        return new ScpiOperationException(
            code,
            exception.Message,
            pointResponse ?? operation?.PointResponse,
            errorQueueResponse ?? operation?.ErrorQueueResponse,
            exception);
    }

    private static string? CombineErrorQueueResponses(IReadOnlyCollection<string> responses) =>
        responses.Count == 0 ? null : string.Join("\n", responses);

    private static string? FormatResponses(string? pointResponse, string? errorQueueResponse)
    {
        if (pointResponse is null) return errorQueueResponse is null ? null : $"error_queue: {errorQueueResponse}";
        if (errorQueueResponse is null) return $"point: {pointResponse}";
        return $"point: {pointResponse}\nerror_queue: {errorQueueResponse}";
    }

    private async Task SafeLogExceptionAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        string operation,
        string? command,
        string? response,
        Exception exception,
        string? commandId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var code = exception is ScpiException scpi ? scpi.ErrorCode : ScpiErrorCodes.TransportError;
        await SafeLogAsync(
            endpoint,
            point,
            operation,
            "Error",
            exception.Message,
            command,
            response,
            ScpiQuality.Bad,
            commandId,
            code,
            stopwatch,
            cancellationToken);
    }

    private async Task SafeLogAsync(
        ScpiEndpointConfig endpoint,
        ScpiPointConfig point,
        string operation,
        string level,
        string message,
        string? command,
        string? response,
        string? quality,
        string? commandId,
        string? errorCode,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            await log.AddScpiAsync(
                endpoint.EndpointId,
                point.PointId,
                operation,
                level,
                message,
                command,
                response,
                quality,
                commandId,
                errorCode,
                (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                cancellationToken);
        }
        catch (Exception logException)
        {
            logger.LogDebug(logException, "Failed to persist SCPI operation log.");
        }
    }
}
