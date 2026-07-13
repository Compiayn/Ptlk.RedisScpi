using Microsoft.Extensions.Options;
using System.Globalization;
using Ptlk.RedisScpi.Configuration;
using Ptlk.RedisScpi.Services.Autocomplete;
using StackExchange.Redis;

namespace Ptlk.RedisScpi.Services.Redis;

public sealed record RedisKeySuggestion(
    string RedisKey,
    string? Type = null,
    string? Access = null,
    string? Unit = null,
    bool HasCompleteMetadata = false);

public sealed class RedisKeySuggestionService(
    RedisConnectionFactory redis,
    IOptions<RedisOptions> options,
    ILogger<RedisKeySuggestionService> logger)
{
    private const int ScanCountPerRequest = 512;

    public async Task<SuggestionPage<RedisKeySuggestion>> SearchPointKeysAsync(
        string query,
        int limit = 24,
        long cursor = 0,
        int pageOffset = 0,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new SuggestionPage<RedisKeySuggestion>([], false);
        }

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var boundedCursor = Math.Max(cursor, 0L);
        var boundedPageOffset = Math.Max(pageOffset, 0);
        var connection = await redis.GetConnectionAsync(cancellationToken);
        var filter = BuildPointKeyFilter(normalizedQuery);
        var database = connection.GetDatabase(options.Value.DatabaseIndex);
        cancellationToken.ThrowIfCancellationRequested();
        var scanResult = (RedisResult[]?)await database.ExecuteAsync(
            "SCAN",
            boundedCursor.ToString(CultureInfo.InvariantCulture),
            "MATCH",
            "point:*",
            "COUNT",
            ScanCountPerRequest.ToString(CultureInfo.InvariantCulture));
        cancellationToken.ThrowIfCancellationRequested();
        if (scanResult is not { Length: >= 2 })
        {
            throw new InvalidOperationException("Redis SCAN returned an unexpected result for point key suggestions.");
        }

        var returnedCursor = long.TryParse(
            scanResult[0].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedCursor)
            ? parsedCursor
            : throw new InvalidOperationException("Redis SCAN returned an invalid cursor for point key suggestions.");
        var rawKeys = (RedisResult[]?)scanResult[1] ?? [];
        var candidates = rawKeys
            .Select(result => result.ToString())
            .Where(text => text.StartsWith("point:", StringComparison.Ordinal)
                           && text["point:".Length..].Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var keys = candidates
            .Skip(boundedPageOffset)
            .Take(boundedLimit)
            .Select(text => (RedisKey)text)
            .ToList();
        var consumedOffset = Math.Min(boundedPageOffset + keys.Count, candidates.Count);
        var hasMoreInPage = consumedOffset < candidates.Count;
        var nextCursor = hasMoreInPage ? boundedCursor : returnedCursor;
        var nextPageOffset = hasMoreInPage ? consumedOffset : 0;

        var suggestions = await Task.WhenAll(keys.Select(key => LoadSuggestionAsync(database, key, cancellationToken)));
        var items = suggestions
            .OrderBy(item => item.RedisKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasMore = hasMoreInPage || returnedCursor != 0;

        return new SuggestionPage<RedisKeySuggestion>(
            items,
            hasMore,
            NextCursor: nextCursor,
            NextPageOffset: nextPageOffset);
    }

    private async Task<RedisKeySuggestion> LoadSuggestionAsync(
        IDatabase database,
        RedisKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RedisValue[] values = await database.HashGetAsync(key, ["type", "access", "unit"]);
            var type = values[0].IsNull ? null : values[0].ToString();
            var access = values[1].IsNull ? null : values[1].ToString();
            var unit = values[2].IsNull ? null : values[2].ToString();
            return new RedisKeySuggestion(
                key.ToString(),
                type,
                access,
                unit,
                type is not null && access is not null && unit is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to load Redis point metadata for {RedisKey}", key);
            return new RedisKeySuggestion(key.ToString());
        }
    }

    private static string BuildPointKeyFilter(string query) =>
        (query.StartsWith("point:", StringComparison.OrdinalIgnoreCase)
            ? query["point:".Length..]
            : query).Trim();
}
