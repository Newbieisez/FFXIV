using System.Text;
using EZBuddy.Core.Settings;

namespace EZBuddy.Core.Tests;

public sealed class RoutineSettingsTests
{
    [Fact]
    public void GcDailyRequiresExplicitAllowlist()
    {
        var settings = new FirstPlayableLoopSettings(
            RunDutyLoop: false,
            RunGrandCompanyExpertDeliveryDaily: true);

        var errors = settings.Validate();

        Assert.Contains(errors, error =>
            error.Contains("requires at least one explicitly approved item ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CustomDeliveriesRequiresExplicitClientSelection()
    {
        var settings = new FirstPlayableLoopSettings(
            RunDutyLoop: false,
            RunCustomDeliveriesWeekly: true);

        var errors = settings.Validate();

        Assert.Contains(errors, error =>
            error.Contains("requires at least one explicitly selected client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task JsonStoreRoundTripsRoutineConfiguration()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonEZBuddySettingsStore(filePath);
            var loop = new FirstPlayableLoopSettings(
                RunDutyLoop: false,
                ApprovedExpertDeliveryItemIds: [101u, 202u],
                RunGrandCompanyExpertDeliveryDaily: true,
                RunVentureRefillDaily: true,
                VentureMinimumQuantity: 15,
                VentureTargetQuantity: 70,
                RunCustomDeliveriesWeekly: true,
                CustomDeliveryClientKeys: ["ameliance", "margrat"],
                CustomDeliveryCraftingClass: "Weaver");

            await store.SaveAsync(
                new EZBuddySettings(loop),
                TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.True(loaded.FirstPlayableLoop.RunGrandCompanyExpertDeliveryDaily);
            Assert.True(loaded.FirstPlayableLoop.RunVentureRefillDaily);
            Assert.Equal(15, loaded.FirstPlayableLoop.VentureMinimumQuantity);
            Assert.Equal(70, loaded.FirstPlayableLoop.VentureTargetQuantity);
            Assert.True(loaded.FirstPlayableLoop.RunCustomDeliveriesWeekly);
            Assert.Equal(["ameliance", "margrat"], loaded.FirstPlayableLoop.EffectiveCustomDeliveryClientKeys);
            Assert.Equal("Weaver", loaded.FirstPlayableLoop.CustomDeliveryCraftingClass);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task OlderSchemaOneJsonUsesSafeRoutineDefaults()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        try
        {
            await File.WriteAllTextAsync(
                filePath,
                "{\"firstPlayableLoop\":{\"runDutyLoop\":false},\"schemaVersion\":1}",
                Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var loaded = await new JsonEZBuddySettingsStore(filePath)
                .LoadAsync(TestContext.Current.CancellationToken);

            Assert.False(loaded.FirstPlayableLoop.RunGrandCompanyExpertDeliveryDaily);
            Assert.False(loaded.FirstPlayableLoop.RunVentureRefillDaily);
            Assert.False(loaded.FirstPlayableLoop.RunCustomDeliveriesWeekly);
            Assert.Equal(10, loaded.FirstPlayableLoop.VentureMinimumQuantity);
            Assert.Equal(50, loaded.FirstPlayableLoop.VentureTargetQuantity);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}