using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class JsonRoutineCompletionStoreTests
{
    [Fact]
    public async Task Record_PersistsAcrossStoreInstances()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "routine-completions.json");
            var completedAt = new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero);
            var first = new JsonRoutineCompletionStore(path);
            await first.RecordAsync(new RoutineCompletion("mini-cactpot", completedAt), token);

            var second = new JsonRoutineCompletionStore(path);
            var loaded = await second.GetLatestAsync("MINI-CACTPOT", token);

            Assert.NotNull(loaded);
            Assert.Equal(completedAt, loaded.CompletedAtUtc);
            Assert.Equal("mini-cactpot", loaded.RoutineKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OlderCompletion_DoesNotOverwriteNewerCompletion()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "routine-completions.json");
            var store = new JsonRoutineCompletionStore(path);
            var newer = new DateTimeOffset(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);
            var older = newer.AddHours(-2);

            await store.RecordAsync(new RoutineCompletion("gc-delivery", newer, "Completed"), token);
            await store.RecordAsync(new RoutineCompletion("gc-delivery", older, "Old"), token);

            var loaded = await store.GetLatestAsync("gc-delivery", token);
            Assert.NotNull(loaded);
            Assert.Equal(newer, loaded.CompletedAtUtc);
            Assert.Equal("Completed", loaded.Outcome);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptLedger_FailsClosedInsteadOfResettingHistory()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "routine-completions.json");
            await File.WriteAllTextAsync(path, "{ this is not valid json", token);
            var store = new JsonRoutineCompletionStore(path);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => store.GetLatestAsync("mini-cactpot", token));

            Assert.Contains("will not silently reset", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Planner_UsesPersistedCompletionAfterRestart()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "routine-completions.json");
            var reset = new RoutineResetRule(RoutineCadence.Daily, new TimeOnly(15, 0));
            var routine = new RoutineDefinition(
                "mini-cactpot",
                "Mini Cactpot",
                EZBuddy.Core.Engine.ActivityCategory.GoldSaucer,
                reset,
                RoutineExecutionKind.Activity);
            var now = new DateTimeOffset(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

            var store = new JsonRoutineCompletionStore(path);
            await store.RecordAsync(new RoutineCompletion("mini-cactpot", now.AddMinutes(-10)), token);

            var restartedPlanner = new ResetAwareRoutinePlanner(new JsonRoutineCompletionStore(path));
            var due = await restartedPlanner.GetDueAsync([routine], now, token);

            var state = Assert.Single(due);
            Assert.False(state.IsDue);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
