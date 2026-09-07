using EZBuddy.Core.Procurement;
using EZBuddy.Core.Runtime;

namespace EZBuddy.Core.Tests;

public sealed class ProcurementAndResumeTests
{
    [Fact]
    public void ProcurementPlanner_ExpandsRecipeAndConsumesOwnedInventory()
    {
        var owned = new Dictionary<uint, int> { [200] = 1 };
        var recipes = new[]
        {
            new ProcurementRecipe(100, 1, [new ProcurementIngredient(200, 2), new ProcurementIngredient(300, 1)])
        };
        var sources = new[]
        {
            new ProcurementSource(200, "gather-ore", ProcurementAction.Gather, 100),
            new ProcurementSource(300, "vendor-glue", ProcurementAction.Vendor, 100)
        };

        var plan = ProcurementPlanner.Build(100, 1, owned, recipes, sources);

        Assert.True(plan.IsComplete);
        Assert.Contains(plan.Steps, step => step.ItemId == 200 && step.Quantity == 1 && step.Action == ProcurementAction.Gather);
        Assert.Contains(plan.Steps, step => step.ItemId == 300 && step.Quantity == 1 && step.Action == ProcurementAction.Vendor);
        Assert.Contains(plan.Steps, step => step.ItemId == 100 && step.Quantity == 1 && step.Action == ProcurementAction.Craft);
    }

    [Fact]
    public void ProcurementPlanner_DetectsRecipeCycles()
    {
        var recipes = new[]
        {
            new ProcurementRecipe(100, 1, [new ProcurementIngredient(200, 1)]),
            new ProcurementRecipe(200, 1, [new ProcurementIngredient(100, 1)])
        };

        var plan = ProcurementPlanner.Build(100, 1, new Dictionary<uint, int>(), recipes, Array.Empty<ProcurementSource>());

        Assert.False(plan.IsComplete);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResumeCheckpoint_RoundTripsAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddyTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "resume.json");
        var store = new JsonResumeCheckpointStore(path);
        var checkpoint = new ResumeCheckpoint(
            "session-1",
            "duty-support-leveling",
            "WaitingForExit",
            ["mini-cactpot", "retainers"],
            new Dictionary<string, string> { ["run"] = "3" },
            DateTimeOffset.UtcNow,
            CleanShutdown: false);

        await store.SaveAsync(checkpoint, cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal("WaitingForExit", loaded.CurrentStage);
        Assert.Contains("mini-cactpot", loaded.CompletedRoutineKeys);

        await store.ClearAsync(cancellationToken);
        Assert.Null(await store.LoadAsync(cancellationToken));
    }

    [Fact]
    public async Task DecisionReplay_WritesJsonLine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "EZBuddyTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "decisions.jsonl");
        var recorder = new JsonLinesDecisionReplayRecorder(path);

        await recorder.RecordAsync(new DecisionReplayEvent(
            "SmartLoot",
            "Pass",
            "Inventory floor reached",
            "Queued",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["itemId"] = "123" }), cancellationToken);

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var line = Assert.Single(lines);
        Assert.Contains("SmartLoot", line, StringComparison.Ordinal);
        Assert.Contains("Inventory floor reached", line, StringComparison.Ordinal);
    }
}
