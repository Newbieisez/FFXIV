using EZBuddy.Core.Adapters;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Gear;

namespace EZBuddy.Core.Tests;

public sealed class SmartGearEquipActivityTests
{
    [Fact]
    public void PlannerQueuesOnlySafeInventoryOrArmoryNonRingUpgrades()
    {
        var items = new[]
        {
            Gear(100, "Old Body", GearSlot.Body, 700, GearStorageLocation.Equipped, isEquipped: true),
            Gear(101, "New Body", GearSlot.Body, 730, GearStorageLocation.ArmoryChest),
            Gear(200, "Old Ring", GearSlot.Ring, 700, GearStorageLocation.Equipped, isEquipped: true),
            Gear(201, "New Ring", GearSlot.Ring, 730, GearStorageLocation.ArmoryChest),
            Gear(300, "Retainer Head", GearSlot.Head, 740, GearStorageLocation.Retainer)
        };

        var gearPlan = SmartGearManager.Build(
            items,
            new GearScoreProfile("Paladin"),
            new GearManagerOptions(730));

        var plan = SmartGearEquipPlanner.Build(gearPlan);

        var instruction = Assert.Single(plan.Instructions);
        Assert.Equal(101u, instruction.Request.ItemId);
        Assert.Equal(GearSlot.Body, instruction.Request.Slot);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Ring", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Warnings, warning => warning.Contains("Retainer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlannerDoesNotQueueEqualScoreSidegrade()
    {
        var items = new[]
        {
            Gear(200, "Equipped Body", GearSlot.Body, 730, GearStorageLocation.Equipped, isEquipped: true),
            Gear(100, "Tie Break Body", GearSlot.Body, 730, GearStorageLocation.ArmoryChest)
        };

        var gearPlan = SmartGearManager.Build(
            items,
            new GearScoreProfile("Paladin"),
            new GearManagerOptions(730));
        var plan = SmartGearEquipPlanner.Build(gearPlan);

        Assert.Empty(plan.Instructions);
        Assert.Contains(plan.Warnings, warning => warning.Contains("does not improve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ActivityAppliesOneUpgradePerStepAndCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var adapter = new FakeGearEquipmentAdapter();
        var plan = new SmartGearEquipPlan(
            [
                Instruction(101, GearSlot.Body, "Body"),
                Instruction(102, GearSlot.Head, "Head")
            ],
            Array.Empty<string>());
        var activity = new SmartGearEquipActivity(adapter, plan);

        Assert.True(await activity.CanExecuteAsync(cancellationToken));

        var first = await activity.ExecuteStepAsync(cancellationToken);
        Assert.Equal(ExecutionDisposition.Continue, first.Disposition);
        Assert.Equal(1, activity.AppliedCount);
        Assert.False(activity.IsComplete);

        var second = await activity.ExecuteStepAsync(cancellationToken);
        Assert.Equal(ExecutionDisposition.Complete, second.Disposition);
        Assert.Equal(2, activity.AppliedCount);
        Assert.True(activity.IsComplete);
        Assert.Equal(new uint[] { 101, 102 }, adapter.EquippedItemIds);
    }

    [Fact]
    public async Task ActivityRetriesWithoutAdvancingWhenEquipFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var adapter = new FakeGearEquipmentAdapter { EquipResult = false };
        var activity = new SmartGearEquipActivity(
            adapter,
            new SmartGearEquipPlan([Instruction(101, GearSlot.Body, "Body")], Array.Empty<string>()));

        var result = await activity.ExecuteStepAsync(cancellationToken);

        Assert.Equal(ExecutionDisposition.Retry, result.Disposition);
        Assert.Equal(0, activity.AppliedCount);
        Assert.False(activity.IsComplete);
    }

    [Fact]
    public async Task ActivityWaitsWhenAdapterIsBusy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var adapter = new FakeGearEquipmentAdapter { Health = AdapterHealth.Busy };
        var activity = new SmartGearEquipActivity(
            adapter,
            new SmartGearEquipPlan([Instruction(101, GearSlot.Body, "Body")], Array.Empty<string>()));

        Assert.False(await activity.CanExecuteAsync(cancellationToken));
        var result = await activity.ExecuteStepAsync(cancellationToken);

        Assert.Equal(ExecutionDisposition.Yield, result.Disposition);
        Assert.Empty(adapter.EquippedItemIds);
    }

    private static GearItemSnapshot Gear(
        uint itemId,
        string name,
        GearSlot slot,
        int itemLevel,
        GearStorageLocation location,
        bool isEquipped = false)
        => new(
            itemId,
            name,
            slot,
            itemLevel,
            ["Paladin"],
            location,
            IsEquipped: isEquipped,
            IsProtected: isEquipped);

    private static GearEquipInstruction Instruction(uint itemId, GearSlot slot, string name)
        => new(
            new GearEquipRequest(itemId, slot, 730),
            name,
            GearStorageLocation.ArmoryChest,
            "Test upgrade");

    private sealed class FakeGearEquipmentAdapter : IGearEquipmentAdapter
    {
        public string Key => "fake-gear";
        public string DisplayName => "Fake Gear";
        public AdapterHealth Health { get; set; } = AdapterHealth.Ready;
        public bool EquipResult { get; set; } = true;
        public List<uint> EquippedItemIds { get; } = new();

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AdapterStatus(
                Key,
                DisplayName,
                Health,
                Health == AdapterHealth.Ready ? "Ready" : "Busy",
                DateTimeOffset.UtcNow));
        }

        public Task<bool> EquipAsync(GearEquipRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EquipResult)
            {
                EquippedItemIds.Add(request.ItemId);
            }

            return Task.FromResult(EquipResult);
        }
    }
}
