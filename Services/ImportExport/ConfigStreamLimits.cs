using System.Text;

namespace Ptlk.RedisScpi.Services.ImportExport;

internal static class ConfigStreamLimits
{
    private const int BufferSize = 81920;

    public static async Task<MemoryStream> BufferAsync(
        Stream source,
        long limitBytes,
        string label,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (limitBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limitBytes));
        }

        if (source.CanSeek && source.Length > limitBytes)
        {
            throw new InvalidDataException($"{label} size exceeds {limitBytes} bytes.");
        }

        var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > limitBytes)
                {
                    throw new InvalidDataException($"{label} size exceeds {limitBytes} bytes.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public static async Task<string> ReadUtf8Async(
        Stream source,
        long limitBytes,
        string label,
        CancellationToken cancellationToken)
    {
        using var buffer = await BufferAsync(source, limitBytes, label, cancellationToken);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(buffer.ToArray());
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"{label} is not valid UTF-8.", ex);
        }
    }
}
