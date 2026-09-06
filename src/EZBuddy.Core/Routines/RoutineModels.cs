using EZBuddy.Core.Engine;

namespace EZBuddy.Core.Routines;

public enum RoutineCadence
{
    Once,
    Daily,
    Weekly
}

public enum RoutineExecutionKind
{
    Activity,
    OrderBotProfile,
    LisbethOrders,
    ExternalAdapter,
    ManualReview
}

public sealed record RoutineResetRule(
    RoutineCadence Cadence,
    TimeOnly ResetTimeUtc,
    DayOfWeek? WeeklyResetDay = null)
{
    public void Validate()
    {
        if (Cadence == RoutineCadence.Weekly && WeeklyResetDay is null)
        {
            throw new InvalidOperationException("Weekly routines require a weekly reset day.");
        }

        if (Cadence != RoutineCadence.Weekly && WeeklyResetDay is not null)
        {
            throw new InvalidOperationException("Only weekly routines may specify a weekly reset day.");
        }
    }
}

public sealed record RoutineDefinition(
    string Key,
    string DisplayName,
    ActivityCategory Category,
    RoutineResetRule ResetRule,
    RoutineExecutionKind ExecutionKind,
    int Priority = 0,
    bool EnabledByDefault = false,
    bool RequiresMagitek = false,
    bool SupportsLisbeth = false,
    bool SupportsOrderBot = false,
    bool DestructiveActionsRequireExplicitOptIn = false,
    string? Description = null);

public sealed record RoutineCompletion(
    string RoutineKey,
    DateTimeOffset CompletedAtUtc,
    string Outcome = "Completed");

public sealed record RoutineDueState(
    RoutineDefinition Routine,
    bool IsDue,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset? NextResetUtc,
    string Reason);

public interface IRoutineCompletionStore
{
    Task<RoutineCompletion?> GetLatestAsync(string routineKey, CancellationToken cancellationToken = default);
    Task RecordAsync(RoutineCompletion completion, CancellationToken cancellationToken = default);
}

public interface IRoutinePlanner
{
    Task<IReadOnlyList<RoutineDueState>> GetDueAsync(
        IEnumerable<RoutineDefinition> routines,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
