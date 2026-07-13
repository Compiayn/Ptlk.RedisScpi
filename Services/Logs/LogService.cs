using Microsoft.EntityFrameworkCore;
using Ptlk.RedisScpi.Data;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Logs;

public sealed class LogService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task AddSystemAsync(
        string category,
        string level,
        string message,
        string? commandId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.SystemLogEntries.Add(new SystemLogEntry
        {
            Category = Limit(category, 80),
            Level = Limit(level, 32),
            Message = Limit(message, 2000),
            CommandId = LimitNullable(commandId, 160)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddScpiAsync(
        string? endpointId,
        string? pointId,
        string operation,
        string level,
        string message,
        string? commandText = null,
        string? responseText = null,
        string? quality = null,
        string? commandId = null,
        string? errorCode = null,
        int? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ScpiLogEntries.Add(new ScpiLogEntry
        {
            EndpointId = LimitNullable(endpointId, 160),
            PointId = LimitNullable(pointId, 160),
            Operation = Limit(operation, 80),
            Level = Limit(level, 32),
            Message = Limit(message, 2000),
            CommandText = LimitNullable(commandText, 2000),
            ResponseText = LimitNullable(responseText, 4000),
            Quality = LimitNullable(quality, 32),
            CommandId = LimitNullable(commandId, 160),
            ErrorCode = LimitNullable(errorCode, 80),
            DurationMs = durationMs
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SystemLogEntry>> GetSystemAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SystemLogEntries.AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScpiLogEntry>> GetScpiAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ScpiLogEntries.AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
    }

    private static string Limit(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static string? LimitNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
