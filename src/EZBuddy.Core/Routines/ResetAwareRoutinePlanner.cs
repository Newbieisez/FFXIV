namespace EZBuddy.Core.Routines;

public sealed class ResetAwareRoutinePlanner : IRoutinePlanner
{
    private readonly IRoutineCompletionStore _completionStore;

    public ResetAwareRoutinePlanner(IRoutineCompletionStore completionStore)
    {
        _completionStore = completionStore ?? throw new ArgumentNullException(nameof(completionStore));
    }

    public async Task<IReadOnlyList<RoutineDueState>> GetDueAsync(
        IEnumerable<RoutineDefinition> routines,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routines);

        var results = new List<RoutineDueState>();
        foreach (var routine in routines.OrderByDescending(routine => routine.Priority).ThenBy(routine => routine.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            routine.ResetRule.Validate();

            var latest = await _completionStore.GetLatestAsync(routine.Key, cancellationToken).ConfigureAwait(false);
            var period = GetCurrentPeriod(routine.ResetRule, nowUtc);
            var isDue = latest is null || latest.CompletedAtUtc < period.StartUtc;
            var reason = latest is null
                ? "No completion has been recorded for this routine."
                : isDue
                    ? $"Last completed {latest.CompletedAtUtc:u}, before the current reset period began."
                    : $"Completed during the current reset period at {latest.CompletedAtUtc:u}.";

            results.Add(new RoutineDueState(routine, isDue, period.StartUtc, period.NextResetUtc, reason));
        }

        return results;
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset? NextResetUtc) GetCurrentPeriod(
        RoutineResetRule rule,
        DateTimeOffset nowUtc)
    {
        rule.Validate();
        var utcNow = nowUtc.ToUniversalTime();

        if (rule.Cadence == RoutineCadence.Once)
        {
            return (DateTimeOffset.MinValue, null);
        }

        if (rule.Cadence == RoutineCadence.Daily)
        {
            var resetToday = new DateTimeOffset(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                rule.ResetTimeUtc.Hour,
                rule.ResetTimeUtc.Minute,
                rule.ResetTimeUtc.Second,
                TimeSpan.Zero);

            var start = utcNow >= resetToday ? resetToday : resetToday.AddDays(-1);
            return (start, start.AddDays(1));
        }

        var weeklyDay = rule.WeeklyResetDay!.Value;
        var daysSinceResetDay = ((int)utcNow.DayOfWeek - (int)weeklyDay + 7) % 7;
        var resetDate = utcNow.Date.AddDays(-daysSinceResetDay);
        var candidate = new DateTimeOffset(
            resetDate.Year,
            resetDate.Month,
            resetDate.Day,
            rule.ResetTimeUtc.Hour,
            rule.ResetTimeUtc.Minute,
            rule.ResetTimeUtc.Second,
            TimeSpan.Zero);

        if (candidate > utcNow)
        {
            candidate = candidate.AddDays(-7);
        }

        return (candidate, candidate.AddDays(7));
    }
}

public sealed class InMemoryRoutineCompletionStore : IRoutineCompletionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RoutineCompletion> _latest = new(StringComparer.OrdinalIgnoreCase);

    public Task<RoutineCompletion?> GetLatestAsync(string routineKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(routineKey);

        lock (_sync)
        {
            _latest.TryGetValue(routineKey, out var completion);
            return Task.FromResult<RoutineCompletion?>(completion);
        }
    }

    public Task RecordAsync(RoutineCompletion completion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentException.ThrowIfNullOrWhiteSpace(completion.RoutineKey);

        lock (_sync)
        {
            if (!_latest.TryGetValue(completion.RoutineKey, out var existing) || completion.CompletedAtUtc >= existing.CompletedAtUtc)
            {
                _latest[completion.RoutineKey] = completion;
            }
        }

        return Task.CompletedTask;
    }
}
