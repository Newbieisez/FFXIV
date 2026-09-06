using EZBuddy.Core.Engine;
using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class RoutinePlannerTests
{
    [Fact]
    public void DailyPeriodRollsAtConfiguredUtcReset()
    {
        var rule = new RoutineResetRule(RoutineCadence.Daily, new TimeOnly(15, 0));
        var beforeReset = new DateTimeOffset(2026, 9, 6, 14, 59, 0, TimeSpan.Zero);
        var afterReset = new DateTimeOffset(2026, 9, 6, 15, 1, 0, TimeSpan.Zero);

        var before = ResetAwareRoutinePlanner.GetCurrentPeriod(rule, beforeReset);
        var after = ResetAwareRoutinePlanner.GetCurrentPeriod(rule, afterReset);

        Assert.Equal(new DateTimeOffset(2026, 9, 5, 15, 0, 0, TimeSpan.Zero), before.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 15, 0, 0, TimeSpan.Zero), after.StartUtc);
    }

    [Fact]
    public void WeeklyPeriodRollsTuesdayAtConfiguredUtcReset()
    {
        var rule = new RoutineResetRule(RoutineCadence.Weekly, new TimeOnly(8, 0), DayOfWeek.Tuesday);
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.Zero);

        var period = ResetAwareRoutinePlanner.GetCurrentPeriod(rule, now);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), period.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.Zero), period.NextResetUtc);
    }

    [Fact]
    public async Task CompletionInsideCurrentPeriodMarksRoutineNotDue()
    {
        var store = new InMemoryRoutineCompletionStore();
        var planner = new ResetAwareRoutinePlanner(store);
        var routine = new RoutineDefinition(
            "test-daily",
            "Test Daily",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Daily, new TimeOnly(15, 0)),
            RoutineExecutionKind.Activity);
        var now = new DateTimeOffset(2026, 9, 6, 18, 0, 0, TimeSpan.Zero);

        await store.RecordAsync(new RoutineCompletion(routine.Key, new DateTimeOffset(2026, 9, 6, 16, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        var states = await planner.GetDueAsync([routine], now, TestContext.Current.CancellationToken);

        Assert.Single(states);
        Assert.False(states[0].IsDue);
    }

    [Fact]
    public async Task PreviousPeriodCompletionMarksRoutineDue()
    {
        var store = new InMemoryRoutineCompletionStore();
        var planner = new ResetAwareRoutinePlanner(store);
        var routine = new RoutineDefinition(
            "test-weekly",
            "Test Weekly",
            ActivityCategory.DailyWeekly,
            new RoutineResetRule(RoutineCadence.Weekly, new TimeOnly(8, 0), DayOfWeek.Tuesday),
            RoutineExecutionKind.Activity);
        var now = new DateTimeOffset(2026, 9, 6, 18, 0, 0, TimeSpan.Zero);

        await store.RecordAsync(new RoutineCompletion(routine.Key, new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        var states = await planner.GetDueAsync([routine], now, TestContext.Current.CancellationToken);

        Assert.Single(states);
        Assert.True(states[0].IsDue);
    }
}
