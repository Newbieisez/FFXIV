using EZBuddy.Core.Materia;
using EZBuddy.Core.Runtime;

namespace EZBuddy.Core.Tests;

public sealed class MateriaAndResumePlannerTests
{
    [Fact]
    public void MateriaPlanner_RespectsCapsAndReserveFloor()
    {
        var request = new GearMeldRequest(
            100,
            "Test Body",
            new Dictionary<string, int> { ["Crit"] = 90 },
            new Dictionary<string, int> { ["Crit"] = 100 },
            new Dictionary<string, int> { ["Crit"] = 100 },
            new Dictionary<string, double> { ["Crit"] = 10 },
            [new MeldSlot(1, IsOvermeld: false)]);

        var plan = MateriaPlanner.Build(request,
        [
            new MateriaStock(10, "Crit Materia", "Crit", 36, QuantityOwned: 2, ReserveQuantity: 1, UnitCost: 100)
        ]);

        Assert.True(plan.MeetsTargets);
        var meld = Assert.Single(plan.Melds);
        Assert.Equal(10, meld.EffectiveStatGain);
        Assert.Equal(100, plan.FinalStats["Crit"]);
        Assert.Equal(100, plan.EstimatedMateriaCost);
    }

    [Fact]
    public void MateriaPlanner_DoesNotUseNonOvermeldMateriaInOvermeldSlot()
    {
        var request = new GearMeldRequest(
            101,
            "Test Ring",
            new Dictionary<string, int> { ["DH"] = 0 },
            new Dictionary<string, int> { ["DH"] = 40 },
            new Dictionary<string, int> { ["DH"] = 20 },
            new Dictionary<string, double> { ["DH"] = 5 },
            [new MeldSlot(1, IsOvermeld: true)]);

        var plan = MateriaPlanner.Build(request,
        [
            new MateriaStock(20, "Restricted DH", "DH", 20, 5, UnitCost: 1, CanOvermeld: false),
            new MateriaStock(21, "Overmeld DH", "DH", 10, 5, UnitCost: 10, CanOvermeld: true)
        ]);

        var meld = Assert.Single(plan.Melds);
        Assert.Equal((uint)21, meld.MateriaItemId);
        Assert.Equal(10, plan.UnmetTargets["DH"]);
    }

    [Fact]
    public void ResumePlanner_CleanShutdownNeedsNoRecovery()
    {
        var checkpoint = Checkpoint(clean: true, current: null);
        var plan = ResumePlanner.Build(checkpoint, new ResumeEnvironmentSnapshot(true, false, false));

        Assert.Equal(ResumeDisposition.NothingToResume, plan.Disposition);
        Assert.False(plan.RequiresUserReview);
    }

    [Fact]
    public void ResumePlanner_BlocksAutomaticRecoveryInsideDuty()
    {
        var checkpoint = Checkpoint(clean: false, current: "Duty Support Leveling");
        var plan = ResumePlanner.Build(checkpoint, new ResumeEnvironmentSnapshot(true, true, true));

        Assert.Equal(ResumeDisposition.ManualReviewRequired, plan.Disposition);
        Assert.True(plan.RequiresUserReview);
    }

    [Fact]
    public void ResumePlanner_RequeuesOnlyExplicitlySafeIdempotentActivity()
    {
        var checkpoint = Checkpoint(clean: false, current: "Maintenance Check");
        var safe = new HashSet<string>(["Maintenance Check"], StringComparer.OrdinalIgnoreCase);
        var plan = ResumePlanner.Build(checkpoint, new ResumeEnvironmentSnapshot(true, false, false, SafeIdempotentActivities: safe));

        Assert.Equal(ResumeDisposition.RequeueFromStart, plan.Disposition);
        Assert.Equal("Maintenance Check", plan.ActivityToRequeue);
    }

    [Fact]
    public void ResumePlanner_RequiresReviewForDestructiveActivity()
    {
        var checkpoint = Checkpoint(clean: false, current: "Mass Desynthesis");
        var destructive = new HashSet<string>(["Mass Desynthesis"], StringComparer.OrdinalIgnoreCase);
        var plan = ResumePlanner.Build(checkpoint, new ResumeEnvironmentSnapshot(true, false, false, DestructiveActivities: destructive));

        Assert.Equal(ResumeDisposition.ManualReviewRequired, plan.Disposition);
        Assert.Contains(plan.Warnings, warning => warning.Contains("inventory", StringComparison.OrdinalIgnoreCase));
    }

    private static ResumeCheckpoint Checkpoint(bool clean, string? current)
        => new(
            "test-session",
            current,
            current is null ? "Idle" : "Running",
            ["Mini Cactpot"],
            null,
            DateTimeOffset.UtcNow,
            CleanShutdown: clean);
}
