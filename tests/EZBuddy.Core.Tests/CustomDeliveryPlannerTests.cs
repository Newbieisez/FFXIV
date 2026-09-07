using EZBuddy.Core.Routines;

namespace EZBuddy.Core.Tests;

public sealed class CustomDeliveryPlannerTests
{
    [Fact]
    public void Planner_EnforcesWeeklyAndPerClientCaps()
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(
            12,
            [
                Client("client-a", "Client A", 6, CustomDeliveryWorkKind.Craft, 100, priority: 20),
                Client("client-b", "Client B", 6, CustomDeliveryWorkKind.Gather, 200, priority: 10),
                Client("client-c", "Client C", 6, CustomDeliveryWorkKind.Craft, 300, priority: 0)
            ]);

        var plan = CustomDeliveryPlanner.Build(snapshot);

        Assert.Equal(12, plan.PlannedDeliveries);
        Assert.Equal(2, plan.Steps.Count);
        Assert.All(plan.Steps, step => Assert.Equal(6, step.DeliveryCount));
        Assert.DoesNotContain(plan.Steps, step => step.ClientKey == "client-c");
    }

    [Fact]
    public void Planner_UsesPriorityBeforeDisplayName()
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(
            6,
            [
                Client("low", "Alpha", 6, CustomDeliveryWorkKind.Craft, 100, priority: 1),
                Client("high", "Zulu", 6, CustomDeliveryWorkKind.Craft, 200, priority: 50)
            ]);

        var plan = CustomDeliveryPlanner.Build(snapshot);

        var step = Assert.Single(plan.Steps);
        Assert.Equal("high", step.ClientKey);
    }

    [Fact]
    public void Planner_RefusesUnknownRequestInsteadOfGuessing()
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(
            6,
            [new CustomDeliveryClientSnapshot("client", "Client", true, 6, CurrentRequest: null)]);

        var plan = CustomDeliveryPlanner.Build(snapshot);

        Assert.Equal(0, plan.PlannedDeliveries);
        Assert.Contains(plan.SkippedClients, message => message.Contains("refusing to guess", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Planner_FishingIsManualOnlyWhenExplicitlyAllowed()
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(
            6,
            [Client("fish", "Fishing Client", 6, CustomDeliveryWorkKind.Fish, 300)]);

        var blocked = CustomDeliveryPlanner.Build(snapshot);
        var allowed = CustomDeliveryPlanner.Build(snapshot, new CustomDeliveryPlannerOptions(AllowManualFishingSteps: true));

        Assert.Equal(0, blocked.PlannedDeliveries);
        var step = Assert.Single(allowed.Steps);
        Assert.Equal(CustomDeliveryExecutionProvider.ManualFishing, step.Provider);
    }

    [Fact]
    public void Planner_RespectsEnabledClientAllowlist()
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(
            12,
            [
                Client("a", "A", 6, CustomDeliveryWorkKind.Craft, 100),
                Client("b", "B", 6, CustomDeliveryWorkKind.Craft, 200)
            ]);
        var options = new CustomDeliveryPlannerOptions(
            EnabledClientKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b" });

        var plan = CustomDeliveryPlanner.Build(snapshot, options);

        var step = Assert.Single(plan.Steps);
        Assert.Equal("b", step.ClientKey);
        Assert.Equal(6, plan.PlannedDeliveries);
    }

    [Fact]
    public void Planner_ComputesRequiredItemQuantity()
    {
        var request = new CustomDeliveryRequest(100, "Requested Item", CustomDeliveryWorkKind.Craft, ItemsPerDelivery: 2);
        var snapshot = new CustomDeliveryWeeklySnapshot(
            3,
            [new CustomDeliveryClientSnapshot("client", "Client", true, 6, request)]);

        var plan = CustomDeliveryPlanner.Build(snapshot);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(3, step.DeliveryCount);
        Assert.Equal(6, step.RequiredItemQuantity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void Snapshot_RejectsInvalidWeeklyAllowance(int allowance)
    {
        var snapshot = new CustomDeliveryWeeklySnapshot(allowance, []);

        Assert.Throws<InvalidDataException>(snapshot.Validate);
    }

    private static CustomDeliveryClientSnapshot Client(
        string key,
        string name,
        int remaining,
        CustomDeliveryWorkKind kind,
        uint itemId,
        int priority = 0)
        => new(
            key,
            name,
            true,
            remaining,
            new CustomDeliveryRequest(itemId, $"Item {itemId}", kind),
            priority);
}
