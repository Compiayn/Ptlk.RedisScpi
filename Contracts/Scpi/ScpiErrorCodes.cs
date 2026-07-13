namespace Ptlk.RedisScpi.Contracts.Scpi;

public static class ScpiErrorCodes
{
    public const string EndpointNotFound = "endpoint_not_found";
    public const string EndpointDisabled = "endpoint_disabled";
    public const string EndpointUnavailable = "endpoint_unavailable";
    public const string PointNotFound = "point_not_found";
    public const string PointDisabled = "point_disabled";
    public const string PointReadonly = "point_readonly";
    public const string OwnershipNotAcquired = "ownership_not_acquired";
    public const string ExpectedVersionMismatch = "expected_version_mismatch";
    public const string InvalidValueType = "invalid_value_type";
    public const string EnumValueNotFound = "enum_value_not_found";
    public const string EnumCodeNotFound = "enum_code_not_found";
    public const string TemplateInvalid = "template_invalid";
    public const string Timeout = "scpi_timeout";
    public const string TransportError = "scpi_transport_error";
    public const string ResponseInvalid = "scpi_response_invalid";
    public const string InstrumentError = "scpi_instrument_error";
    public const string WriteVerificationFailed = "write_verification_failed";
    public const string PointStateInvalid = "point_state_invalid";
    public const string ConfigurationInvalid = "configuration_invalid";
    public const string InvalidPayload = "invalid_payload";
    public const string CommandIdPayloadMismatch = "command_id_payload_mismatch";
}

public class ScpiException(string errorCode, string message, Exception? inner = null) : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class ScpiTransportException(string message, Exception? inner = null)
    : ScpiException(ScpiErrorCodes.TransportError, message, inner);

public sealed class ScpiTimeoutException(string message, Exception? inner = null)
    : ScpiException(ScpiErrorCodes.Timeout, message, inner);

public sealed class ScpiTemplateException(string message)
    : ScpiException(ScpiErrorCodes.TemplateInvalid, message);

public sealed class ScpiValidationException(string errorCode, string message)
    : ScpiException(errorCode, message);

public sealed class ScpiParseException(string message, string? rawResponse = null)
    : ScpiException(ScpiErrorCodes.ResponseInvalid, message)
{
    public string? RawResponse { get; } = rawResponse;
}

public sealed class ScpiInstrumentException(string message, string rawResponse)
    : ScpiException(ScpiErrorCodes.InstrumentError, message)
{
    public string RawResponse { get; } = rawResponse;
}

public sealed class ScpiOperationException(
    string errorCode,
    string message,
    string? pointResponse,
    string? errorQueueResponse,
    Exception? inner = null) : ScpiException(errorCode, message, inner)
{
    public string? PointResponse { get; } = pointResponse;
    public string? ErrorQueueResponse { get; } = errorQueueResponse;
}

public sealed class ScpiWriteOperationException(
    string errorCode,
    string message,
    bool commandWasSent,
    Exception? inner = null,
    string? pointResponse = null,
    string? errorQueueResponse = null) : ScpiException(errorCode, message, inner)
{
    public bool CommandWasSent { get; } = commandWasSent;
    public string? PointResponse { get; } = pointResponse;
    public string? ErrorQueueResponse { get; } = errorQueueResponse;
}
