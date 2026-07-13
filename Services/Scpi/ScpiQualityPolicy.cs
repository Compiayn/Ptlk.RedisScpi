using Ptlk.RedisScpi.Contracts.Scpi;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed record ScpiFailureClassification(
    string Quality,
    string ErrorCode,
    bool EndpointWide);

public sealed class ScpiQualityPolicy
{
    public ScpiFailureClassification Classify(Exception exception)
    {
        var code = exception is ScpiException scpi
            ? scpi.ErrorCode
            : ScpiErrorCodes.TransportError;
        var endpointWide = code is ScpiErrorCodes.Timeout or ScpiErrorCodes.TransportError or ScpiErrorCodes.EndpointUnavailable;
        return new ScpiFailureClassification(ScpiQuality.Bad, code, endpointWide);
    }
}
