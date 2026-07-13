using System.Text;
using Microsoft.EntityFrameworkCore;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Services.Autocomplete;

namespace Ptlk.RedisScpi.Services.Paths;

public sealed record SourcePathSuggestion(
    string SourcePath,
    string EndpointId,
    string PointId,
    string Name,
    string? DisplayName,
    string DataType,
    string Access,
    bool Enabled);

public sealed class PathSuggestionService(IDbContextFactory<AppDbContext> dbFactory)
{
    public Task<SuggestionPage<SourcePathSuggestion>> SearchPointSuggestionsAsync(
        string query,
        int limit = 24,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        SearchScpiPointSuggestionsAsync(query, limit, offset, cancellationToken);

    public async Task<SuggestionPage<SourcePathSuggestion>> SearchScpiPointSuggestionsAsync(
        string query,
        int limit = 24,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim() ?? "";
        if (normalizedQuery.Length == 0)
        {
            return new SuggestionPage<SourcePathSuggestion>([], false);
        }

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var boundedOffset = Math.Max(offset, 0);
        var pattern = $"%{EscapeLikePattern(normalizedQuery.ToLowerInvariant())}%";

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.ScpiPointConfigs
            .AsNoTracking()
            .Where(point =>
                EF.Functions.Like(point.SourcePath.ToLower(), pattern, "\\")
                || EF.Functions.Like(point.PointId.ToLower(), pattern, "\\")
                || EF.Functions.Like(point.Name.ToLower(), pattern, "\\")
                || (point.DisplayName != null && EF.Functions.Like(point.DisplayName.ToLower(), pattern, "\\"))
                || (point.EndpointConfig != null
                    && EF.Functions.Like(point.EndpointConfig.EndpointId.ToLower(), pattern, "\\")))
            .OrderBy(point => point.EndpointConfig!.EndpointId)
            .ThenBy(point => point.PointId)
            .Skip(boundedOffset)
            .Take(boundedLimit + 1)
            .Select(point => new SourcePathSuggestion(
                point.SourcePath,
                point.EndpointConfig!.EndpointId,
                point.PointId,
                point.Name,
                point.DisplayName,
                point.DataType,
                point.Access,
                point.Enabled))
            .ToListAsync(cancellationToken);

        var items = candidates.Take(boundedLimit).ToList();
        return new SuggestionPage<SourcePathSuggestion>(
            items,
            candidates.Count > boundedLimit,
            boundedOffset + items.Count);
    }

    public async Task<IReadOnlyList<string>> ListSourcePathsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ScpiPointConfigs
            .AsNoTracking()
            .OrderBy(point => point.SourcePath)
            .Select(point => point.SourcePath)
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLikePattern(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
