namespace Ptlk.RedisScpi.Services.Ui;

public enum ScreenAlertSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ScreenAlertMessage(
    Guid Id,
    ScreenAlertSeverity Severity,
    string Message,
    string? Title,
    TimeSpan Duration,
    DateTimeOffset CreatedAt);

public sealed class ScreenAlertService
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly Queue<ScreenAlertMessage> _pending = new();

    public event Action? AlertsChanged;

    public Task ShowAsync(
        string message,
        ScreenAlertSeverity severity = ScreenAlertSeverity.Info,
        string? title = null,
        TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var alert = new ScreenAlertMessage(
            Guid.NewGuid(),
            severity,
            message.Trim(),
            string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            duration is null || duration < MinimumDuration ? MinimumDuration : duration.Value,
            DateTimeOffset.UtcNow);
        lock (_sync)
        {
            _pending.Enqueue(alert);
        }

        AlertsChanged?.Invoke();
        return Task.CompletedTask;
    }

    public IReadOnlyList<ScreenAlertMessage> Drain()
    {
        lock (_sync)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            var messages = _pending.ToArray();
            _pending.Clear();
            return messages;
        }
    }
}
