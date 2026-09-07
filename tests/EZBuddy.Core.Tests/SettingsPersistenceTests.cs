using EZBuddy.Core.Adapters;
using EZBuddy.Core.Settings;

namespace EZBuddy.Core.Tests;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task JsonStore_RoundTripsPerProfileSettingsAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new DefaultSettingsStoragePathProvider(root);
            var store = new JsonEZBuddySettingsStore(provider, "Test Character/Unsafe");
            var expected = new EZBuddySettings(new FirstPlayableLoopSettings(
                DutyId: 123,
                DutyProfilePath: "C:\\Profiles\\Duty.xml",
                DutyMode: DutyAutomationMode.Trust,
                TrustId: 2,
                TargetLevel: 100,
                MaxRuns: 4,
                MinimumDutyFreeSlots: 8,
                InventoryTargetFreeSlots: 14,
                ApprovedExpertDeliveryItemIds: [1001u, 1002u]));

            await store.SaveAsync(expected);
            var loaded = await store.LoadAsync();

            Assert.Equal(expected.FirstPlayableLoop.DutyId, loaded.FirstPlayableLoop.DutyId);
            Assert.Equal(expected.FirstPlayableLoop.DutyMode, loaded.FirstPlayableLoop.DutyMode);
            Assert.Equal(expected.FirstPlayableLoop.MaxRuns, loaded.FirstPlayableLoop.MaxRuns);
            Assert.Equal([1001u, 1002u], loaded.FirstPlayableLoop.EffectiveApprovedExpertDeliveryItemIds);
            Assert.True(File.Exists(store.FilePath));
            Assert.False(File.Exists(store.FilePath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ObservableManager_DebouncesChangesAndFlushesLatestSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new DefaultSettingsStoragePathProvider(root);
            var store = new JsonEZBuddySettingsStore(provider, "Debounce Character");
            await using var manager = new JsonEZBuddySettingsManager(store, TimeSpan.FromMilliseconds(20));

            await manager.LoadAsync();
            manager.Update(current => current with
            {
                FirstPlayableLoop = current.FirstPlayableLoop with { MaxRuns = 2 }
            });
            manager.Update(current => current with
            {
                FirstPlayableLoop = current.FirstPlayableLoop with { MaxRuns = 7 }
            });

            await manager.FlushAsync();

            var loaded = await store.LoadAsync();
            Assert.Equal(7, loaded.FirstPlayableLoop.MaxRuns);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
