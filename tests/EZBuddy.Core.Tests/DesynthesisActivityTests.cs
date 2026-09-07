using EZBuddy.Core.Engine;
using EZBuddy.Core.Inventory;

namespace EZBuddy.Core.Tests;

public sealed class DesynthesisActivityTests
{
    [Fact]
    public async Task Activity_ProcessesOnlyExplicitlyAllowlistedItemOnePerTick()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHost([
            Candidate("slot-a", 100, "Allowed", itemLevel: 20),
            Candidate("slot-b", 200, "Not Allowed", itemLevel: 10)
        ]);
        var policy = new InventoryMaintenanceSettings(
            MaximumDesynthesisItemLevel: 50,
            AllowDesynthesis: true,
            DesynthesisAllowlist: new HashSet<uint> { 100 });
        var activity = new DesynthesisActivity(host, new DesynthesisActivityOptions(policy));

        var first = await activity.ExecuteStepAsync(token);
        var second = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.Equal(ExecutionDisposition.Complete, second.Disposition);
        Assert.Equal(["slot-a"], host.DesynthesizedInstanceKeys);
        Assert.Equal(1, activity.ProcessedCount);
    }

    [Fact]
    public async Task ProtectedOrEquippedItem_IsNeverDesynthesized()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHost([
            Candidate("protected", 100, "Protected", itemLevel: 20, isProtected: true),
            Candidate("equipped", 100, "Equipped", itemLevel: 20, isEquipped: true)
        ]);
        var policy = new InventoryMaintenanceSettings(
            MaximumDesynthesisItemLevel: 50,
            AllowDesynthesis: true,
            DesynthesisAllowlist: new HashSet<uint> { 100 });
        var activity = new DesynthesisActivity(host, new DesynthesisActivityOptions(policy));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.Empty(host.DesynthesizedInstanceKeys);
    }

    [Fact]
    public async Task CandidateIsRecheckedImmediatelyBeforeDestructiveCall()
    {
        var token = TestContext.Current.CancellationToken;
        var initial = Candidate("slot-a", 100, "Allowed", itemLevel: 20);
        var changed = Candidate("slot-a", 100, "Now Protected", itemLevel: 20, isProtected: true);
        var host = new ChangingHost(initial, changed);
        var policy = new InventoryMaintenanceSettings(
            MaximumDesynthesisItemLevel: 50,
            AllowDesynthesis: true,
            DesynthesisAllowlist: new HashSet<uint> { 100 });
        var activity = new DesynthesisActivity(host, new DesynthesisActivityOptions(policy));

        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Yield, result.Disposition);
        Assert.False(host.DesynthesisCalled);
    }

    [Fact]
    public async Task SafetyCapStopsFurtherDesynthesis()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHost([
            Candidate("slot-a", 100, "A", 20),
            Candidate("slot-b", 100, "B", 20)
        ]);
        var policy = new InventoryMaintenanceSettings(
            MaximumDesynthesisItemLevel: 50,
            AllowDesynthesis: true,
            DesynthesisAllowlist: new HashSet<uint> { 100 });
        var activity = new DesynthesisActivity(host, new DesynthesisActivityOptions(policy, MaximumItemsPerRun: 1));

        await activity.ExecuteStepAsync(token);
        var result = await activity.ExecuteStepAsync(token);

        Assert.Equal(ExecutionDisposition.Complete, result.Disposition);
        Assert.Single(host.DesynthesizedInstanceKeys);
    }

    [Fact]
    public void Options_RejectDesynthesisWithoutExplicitAllowlist()
    {
        var policy = new InventoryMaintenanceSettings(
            MaximumDesynthesisItemLevel: 50,
            AllowDesynthesis: true);

        Assert.Throws<ArgumentException>(() =>
            new DesynthesisActivity(new FakeHost([]), new DesynthesisActivityOptions(policy)));
    }

    private static DesynthesisCandidate Candidate(
        string key,
        uint itemId,
        string name,
        int itemLevel,
        bool isProtected = false,
        bool isEquipped = false)
        => new(
            key,
            new InventoryItemSnapshot(
                itemId,
                name,
                itemLevel,
                DurabilityPercent: 100,
                SpiritbondPercent: 0,
                CanRepair: false,
                CanExtractMateria: false,
                CanDesynthesize: true,
                IsEquipped: isEquipped,
                IsProtected: isProtected));

    private sealed class FakeHost : IDesynthesisHost
    {
        private readonly List<DesynthesisCandidate> _candidates;

        public FakeHost(IEnumerable<DesynthesisCandidate> candidates)
        {
            _candidates = candidates.ToList();
        }

        public List<string> DesynthesizedInstanceKeys { get; } = [];

        public Task<IReadOnlyList<DesynthesisCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DesynthesisCandidate>>(
                _candidates.Where(candidate => !DesynthesizedInstanceKeys.Contains(candidate.InstanceKey)).ToArray());
        }

        public Task<bool> DesynthesizeAsync(DesynthesisCandidate candidate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DesynthesizedInstanceKeys.Add(candidate.InstanceKey);
            return Task.FromResult(true);
        }

        public Task<bool> IsBusyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class ChangingHost : IDesynthesisHost
    {
        private readonly DesynthesisCandidate _initial;
        private readonly DesynthesisCandidate _changed;
        private int _reads;

        public ChangingHost(DesynthesisCandidate initial, DesynthesisCandidate changed)
        {
            _initial = initial;
            _changed = changed;
        }

        public bool DesynthesisCalled { get; private set; }

        public Task<IReadOnlyList<DesynthesisCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _reads++;
            return Task.FromResult<IReadOnlyList<DesynthesisCandidate>>([_reads == 1 ? _initial : _changed]);
        }

        public Task<bool> DesynthesizeAsync(DesynthesisCandidate candidate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DesynthesisCalled = true;
            return Task.FromResult(true);
        }

        public Task<bool> IsBusyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }
}
