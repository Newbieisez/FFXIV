using EZBuddy.Core.Diagnostics;

namespace EZBuddy.Core.Tests;

public sealed class ConflictGuardTests
{
    [Fact]
    public async Task CriticalDutyConflict_IsMarkedToPauseAffectedWork()
    {
        var token = TestContext.Current.CancellationToken;
        var source = new StaticComponentSource(
        [
            new RuntimeComponent("PandaFarmer", "Plugin", true, false),
            new RuntimeComponent("Magitek", "CombatRoutine", true, true)
        ]);
        var guard = new ConflictGuard(source);

        var findings = await guard.ScanAsync(["Duty"], token);

        var duty = Assert.Single(findings, finding => finding.RuleId == "duty.overlap");
        Assert.Equal(ConflictSeverity.Critical, duty.Severity);
        Assert.True(duty.ShouldPause);
    }

    [Fact]
    public async Task WarningRetainerConflict_DoesNotRequestForcedPause()
    {
        var token = TestContext.Current.CancellationToken;
        var source = new StaticComponentSource(
        [
            new RuntimeComponent("RetainerMaid", "Plugin", true, false)
        ]);
        var guard = new ConflictGuard(source);

        var findings = await guard.ScanAsync(["Retainers"], token);

        var retainer = Assert.Single(findings, finding => finding.RuleId == "retainer.overlap");
        Assert.Equal(ConflictSeverity.Warning, retainer.Severity);
        Assert.False(retainer.ShouldPause);
    }

    [Fact]
    public async Task DisabledComponent_DoesNotCreateFinding()
    {
        var token = TestContext.Current.CancellationToken;
        var source = new StaticComponentSource(
        [
            new RuntimeComponent("PandaFarmer", "Plugin", false, false)
        ]);
        var guard = new ConflictGuard(source);

        var findings = await guard.ScanAsync(["Duty"], token);

        Assert.Empty(findings);
    }

    private sealed class StaticComponentSource(IReadOnlyList<RuntimeComponent> components) : IRuntimeComponentSource
    {
        public Task<IReadOnlyList<RuntimeComponent>> GetComponentsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(components);
        }
    }
}
