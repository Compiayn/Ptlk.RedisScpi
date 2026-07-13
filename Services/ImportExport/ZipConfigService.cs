using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ptlk.RedisScpi.Configuration;

namespace Ptlk.RedisScpi.Services.ImportExport;

public sealed record RedisScpiConfigManifest(
    int Version,
    string Format,
    string ConfigurationFile,
    string ImportMode,
    IReadOnlyList<string> KindOrder);

public sealed class ZipConfigService(
    CsvConfigService csv,
    IOptions<ImportExportOptions> options)
{
    public const int ManifestVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string CsvEntryName = "redis-scpi-config.csv";
    public const string ManifestFormat = "ptlk.redis-scpi.config";
    public const string ManifestImportMode = "merge-upsert";

    private static readonly string[] CanonicalKindOrder =
    [
        ScpiConfigCsvSchema.EndpointKind,
        ScpiConfigCsvSchema.PointKind,
        ScpiConfigCsvSchema.EnumOptionKind,
        ScpiConfigCsvSchema.MappingKind
    ];

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<Stream> ExportAsync(CancellationToken cancellationToken = default)
    {
        await using var csvStream = await csv.ExportAsync(cancellationToken);
        using var manifestStream = CreateManifestStream();

        var extractedLength = CheckedSum(csvStream.Length, manifestStream.Length);
        if (extractedLength > options.Value.ZipExtractedLimitBytes)
        {
            throw new InvalidOperationException(
                $"Exported ZIP content exceeds {options.Value.ZipExtractedLimitBytes} extracted bytes.");
        }

        var output = new MemoryStream();
        try
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var csvEntry = archive.CreateEntry(CsvEntryName, CompressionLevel.Optimal);
                await using (var entryStream = csvEntry.Open())
                {
                    csvStream.Position = 0;
                    await csvStream.CopyToAsync(entryStream, cancellationToken);
                }

                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (var entryStream = manifestEntry.Open())
                {
                    manifestStream.Position = 0;
                    await manifestStream.CopyToAsync(entryStream, cancellationToken);
                }
            }

            if (output.Length > options.Value.ZipFileLimitBytes)
            {
                throw new InvalidOperationException(
                    $"Exported ZIP size exceeds {options.Value.ZipFileLimitBytes} bytes.");
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

    public async Task<CsvImportResult> ImportAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var bufferedZip = await ConfigStreamLimits.BufferAsync(
                zipStream,
                options.Value.ZipFileLimitBytes,
                "ZIP",
                cancellationToken);
            using var archive = new ZipArchive(bufferedZip, ZipArchiveMode.Read, leaveOpen: true);

            var fileEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
            if (archive.Entries.Count != 2
                || fileEntries.Count != 2
                || fileEntries.Count(entry => entry.FullName == CsvEntryName) != 1
                || fileEntries.Count(entry => entry.FullName == ManifestEntryName) != 1)
            {
                return Failure(
                    $"ZIP must contain exactly '{ManifestEntryName}' and '{CsvEntryName}' at its root.");
            }

            var csvEntry = fileEntries.Single(entry => entry.FullName == CsvEntryName);
            var manifestEntry = fileEntries.Single(entry => entry.FullName == ManifestEntryName);

            var extractedLength = CheckedSum(fileEntries.Select(entry => entry.Length));
            if (extractedLength > options.Value.ZipExtractedLimitBytes)
            {
                return Failure(
                    $"ZIP extracted size exceeds {options.Value.ZipExtractedLimitBytes} bytes.");
            }

            if (csvEntry.Length > options.Value.SingleCsvLimitBytes)
            {
                return Failure($"CSV entry exceeds {options.Value.SingleCsvLimitBytes} bytes.");
            }

            RedisScpiConfigManifest? manifest;
            await using (var entryStream = manifestEntry.Open())
            {
                var manifestJson = await ConfigStreamLimits.ReadUtf8Async(
                    entryStream,
                    options.Value.ZipExtractedLimitBytes,
                    "Manifest",
                    cancellationToken);
                manifest = JsonSerializer.Deserialize<RedisScpiConfigManifest>(manifestJson, ManifestJsonOptions);
            }

            var manifestError = ValidateManifest(manifest);
            if (manifestError is not null)
            {
                return Failure(manifestError);
            }

            await using var configStream = csvEntry.Open();
            return await csv.ImportAsync(configStream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            return Failure(ex.Message);
        }
        catch (JsonException ex)
        {
            return Failure($"Manifest is not valid JSON: {ex.Message}");
        }
        catch (OverflowException)
        {
            return Failure("ZIP extracted size is too large.");
        }
    }

    private static MemoryStream CreateManifestStream()
    {
        var manifest = new RedisScpiConfigManifest(
            ManifestVersion,
            ManifestFormat,
            CsvEntryName,
            ManifestImportMode,
            CanonicalKindOrder);
        return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions), writable: false);
    }

    private static string? ValidateManifest(RedisScpiConfigManifest? manifest)
    {
        if (manifest is null)
        {
            return "Manifest is empty.";
        }

        if (manifest.Version != ManifestVersion)
        {
            return $"Manifest version {manifest.Version} is not supported.";
        }

        if (!string.Equals(manifest.Format, ManifestFormat, StringComparison.Ordinal))
        {
            return $"Manifest format must be '{ManifestFormat}'.";
        }

        if (!string.Equals(manifest.ConfigurationFile, CsvEntryName, StringComparison.Ordinal))
        {
            return $"Manifest configurationFile must be '{CsvEntryName}'.";
        }

        if (!string.Equals(manifest.ImportMode, ManifestImportMode, StringComparison.Ordinal))
        {
            return $"Manifest importMode must be '{ManifestImportMode}'.";
        }

        if (manifest.KindOrder is null || !manifest.KindOrder.SequenceEqual(CanonicalKindOrder, StringComparer.Ordinal))
        {
            return "Manifest kindOrder must be endpoint, point, enum_option, mapping.";
        }

        return null;
    }

    private static CsvImportResult Failure(string error) => new(0, [error]);

    private static long CheckedSum(params long[] values) => CheckedSum((IEnumerable<long>)values);

    private static long CheckedSum(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total = checked(total + value);
        }

        return total;
    }
}
