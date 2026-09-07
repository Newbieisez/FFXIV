namespace EZBuddy.Core.Planning
{
    public enum PlanRisk
    {
        Low,
        Medium,
        High,
        ManualReview
    }

    public sealed record PlannedWork(
        string Key,
        string Title,
        int Priority,
        PlanRisk Risk,
        string Reason,
        IReadOnlyDictionary<string, string>? Metadata = null);
}

namespace EZBuddy.Core.DeepDungeons
{
    using EZBuddy.Core.Planning;

    public sealed record DeepDungeonSnapshot(
        string DungeonKey,
        int CurrentFloor,
        int FloorSetEnd,
        int AetherpoolArm,
        int AetherpoolArmor,
        int FreeInventorySlots,
        int MinimumFreeInventorySlots,
        bool PassageOpen,
        bool CairnOfReturnNeeded,
        bool PartyMemberKO,
        bool BossFloor,
        IReadOnlyDictionary<string, int> Pomanders);

    public sealed record DeepDungeonOptions(
        int MinimumAetherpool = 0,
        bool OpenSilverChestsWhenSafe = true,
        bool OpenBronzeChests = false,
        bool PrioritizeExitWhenInventoryTight = true);

    public static class DeepDungeonPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(DeepDungeonSnapshot snapshot, DeepDungeonOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            options ??= new DeepDungeonOptions();
            if (string.IsNullOrWhiteSpace(snapshot.DungeonKey) || snapshot.CurrentFloor <= 0 || snapshot.FloorSetEnd < snapshot.CurrentFloor)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            var work = new List<PlannedWork>();
            if (snapshot.PartyMemberKO && snapshot.CairnOfReturnNeeded)
            {
                work.Add(new PlannedWork("revive", "Use Cairn of Return", 1000, PlanRisk.High,
                    "A party member is KO and revival is required before ordinary progression."));
                return work;
            }

            if (snapshot.BossFloor)
            {
                work.Add(new PlannedWork("boss", "Clear floor-set boss", 900, PlanRisk.High,
                    "Boss floor detected; combat provider owns rotation/mechanics while the planner suppresses chest detours."));
                return work;
            }

            if (snapshot.FreeInventorySlots <= snapshot.MinimumFreeInventorySlots && options.PrioritizeExitWhenInventoryTight)
            {
                work.Add(new PlannedWork("exit", "Prioritize Passage", 800, PlanRisk.Medium,
                    "Inventory is at the configured safety floor; optional chest detours are suppressed."));
            }

            if (snapshot.PassageOpen)
            {
                work.Add(new PlannedWork("passage", "Advance through Passage", 700, PlanRisk.Low,
                    "Passage is open and advancing is the safest deterministic progress action."));
            }
            else
            {
                work.Add(new PlannedWork("mobs", "Defeat required enemies", 600, PlanRisk.Medium,
                    "Passage is not open; continue objective combat until the unlock condition is met."));
            }

            var aetherpoolLow = snapshot.AetherpoolArm < options.MinimumAetherpool || snapshot.AetherpoolArmor < options.MinimumAetherpool;
            if (options.OpenSilverChestsWhenSafe && snapshot.FreeInventorySlots > snapshot.MinimumFreeInventorySlots)
            {
                work.Add(new PlannedWork("silver", "Consider silver chest", aetherpoolLow ? 650 : 350, PlanRisk.Medium,
                    aetherpoolLow ? "Aetherpool is below the configured target." : "Silver chest is optional because Aetherpool target is already met."));
            }

            if (options.OpenBronzeChests && snapshot.FreeInventorySlots > snapshot.MinimumFreeInventorySlots)
            {
                work.Add(new PlannedWork("bronze", "Consider bronze chest", 200, PlanRisk.Medium,
                    "Optional loot detour is enabled and inventory capacity is above the safety floor."));
            }

            return work.OrderByDescending(item => item.Priority).ToArray();
        }
    }
}

namespace EZBuddy.Core.TreasureHunts
{
    using EZBuddy.Core.Planning;

    public enum TreasureMapState
    {
        NoneOwned,
        Undeciphered,
        Deciphered,
        DigLocationKnown,
        ChestSpawned,
        PortalAvailable,
        Complete
    }

    public sealed record TreasureHuntSnapshot(
        TreasureMapState State,
        uint? MapItemId,
        bool DecipherCooldownReady,
        bool HasVerifiedAcquisitionSource,
        bool HasVerifiedTravelTarget,
        bool InCombat,
        bool PartyRecommended,
        int PartySize,
        bool PortalSupported);

    public static class TreasureHuntPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(TreasureHuntSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return snapshot.State switch
            {
                TreasureMapState.NoneOwned =>
                    [new("acquire", "Acquire configured treasure map", 700,
                        snapshot.HasVerifiedAcquisitionSource ? PlanRisk.Low : PlanRisk.ManualReview,
                        snapshot.HasVerifiedAcquisitionSource ? "A verified acquisition source is available." : "No verified map acquisition source is configured.")],
                TreasureMapState.Undeciphered when !snapshot.DecipherCooldownReady =>
                    [new("cooldown", "Wait for Decipher", 700, PlanRisk.Low, "Decipher cooldown is not ready; do not consume another map.")],
                TreasureMapState.Undeciphered =>
                    [new("decipher", "Decipher map", 700, PlanRisk.Low, "Map is owned and Decipher is ready.")],
                TreasureMapState.Deciphered or TreasureMapState.DigLocationKnown =>
                    [new("travel-dig", "Travel and Dig", 700,
                        snapshot.HasVerifiedTravelTarget ? PlanRisk.Medium : PlanRisk.ManualReview,
                        snapshot.HasVerifiedTravelTarget ? "A verified target location is available." : "Treasure coordinates require a verified travel target before movement.")],
                TreasureMapState.ChestSpawned when snapshot.InCombat =>
                    [new("defend", "Defend treasure chest", 800, PlanRisk.High, "Chest defense encounter is active; combat provider owns execution.")],
                TreasureMapState.ChestSpawned =>
                    [new("open", "Open resolved treasure chest", 700, PlanRisk.Medium, "Defense is complete and the chest can be resolved.")],
                TreasureMapState.PortalAvailable when snapshot.PartyRecommended && snapshot.PartySize <= 1 =>
                    [new("portal-review", "Portal requires party review", 900, PlanRisk.ManualReview, "Configured policy recommends a party before entering this portal dungeon.")],
                TreasureMapState.PortalAvailable =>
                    [new("portal", "Enter treasure portal", 700,
                        snapshot.PortalSupported ? PlanRisk.High : PlanRisk.ManualReview,
                        snapshot.PortalSupported ? "Portal route is supported by the configured provider." : "Portal dungeon has no verified EZBuddy provider.")],
                _ => Array.Empty<PlannedWork>()
            };
        }
    }
}

namespace EZBuddy.Core.FieldOperations
{
    using EZBuddy.Core.Planning;

    public sealed record FieldEventSnapshot(
        string Key,
        string Name,
        int RewardScore,
        int EstimatedMinutes,
        int DangerScore,
        bool Active,
        bool Reachable,
        bool RequiresParty = false,
        bool IsCriticalEngagement = false);

    public sealed record FieldOperationSnapshot(
        string ZoneKey,
        int ProgressValue,
        int ProgressFloorToProtect,
        bool HasRequiredLoadout,
        int PartySize,
        IReadOnlyList<FieldEventSnapshot> Events);

    public static class FieldOperationPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(FieldOperationSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (string.IsNullOrWhiteSpace(snapshot.ZoneKey) || snapshot.ProgressValue < 0 || snapshot.ProgressFloorToProtect < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            if (!snapshot.HasRequiredLoadout)
            {
                return [new("loadout", "Restore configured field-operation loadout", 1000, PlanRisk.ManualReview,
                    "Required actions/items are not available; event automation is blocked until the loadout is validated.")];
            }

            var protectProgress = snapshot.ProgressValue <= snapshot.ProgressFloorToProtect;
            return snapshot.Events
                .Where(evt => evt.Active && evt.Reachable)
                .Select(evt =>
                {
                    var partyBlocked = evt.RequiresParty && snapshot.PartySize <= 1;
                    var score = (evt.RewardScore * 100d) / Math.Max(1, evt.EstimatedMinutes) - evt.DangerScore * (protectProgress ? 30 : 10);
                    return new PlannedWork(
                        evt.Key,
                        evt.Name,
                        (int)Math.Round(score),
                        partyBlocked ? PlanRisk.ManualReview : evt.IsCriticalEngagement ? PlanRisk.High : PlanRisk.Medium,
                        partyBlocked
                            ? "Event requires a party under the configured policy."
                            : protectProgress
                                ? "Progress protection is active, so high-danger events are heavily deprioritized."
                                : "Ranked by reward-per-minute with a danger penalty.");
                })
                .OrderByDescending(item => item.Priority)
                .ToArray();
        }
    }
}

namespace EZBuddy.Core.SharedFates
{
    using EZBuddy.Core.Planning;

    public sealed record SharedFateRegionSnapshot(
        string RegionKey,
        string Name,
        int CompletedFates,
        int RequiredFates,
        bool HasActiveReachableFate,
        int EstimatedMinutesToNextCompletion,
        int Priority = 0);

    public sealed record SharedFateSnapshot(
        int Gemstones,
        int GemstoneCap,
        int ProjectedGemstones,
        int SafetyBuffer,
        IReadOnlyList<SharedFateRegionSnapshot> Regions,
        bool HasApprovedSpendRule);

    public static class SharedFatePlanner
    {
        public static IReadOnlyList<PlannedWork> Build(SharedFateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.Gemstones < 0 || snapshot.GemstoneCap <= 0 || snapshot.SafetyBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            var work = new List<PlannedWork>();
            var safeCeiling = snapshot.GemstoneCap - snapshot.SafetyBuffer;
            if (snapshot.Gemstones + snapshot.ProjectedGemstones > safeCeiling)
            {
                work.Add(new PlannedWork(
                    "spend",
                    "Protect Bicolor Gemstone cap",
                    1000,
                    snapshot.HasApprovedSpendRule ? PlanRisk.Low : PlanRisk.ManualReview,
                    snapshot.HasApprovedSpendRule
                        ? "Projected gemstone income exceeds the configured safe ceiling; execute only the approved spend rule."
                        : "Projected gemstone income exceeds the safe ceiling, but no approved spend rule exists."));
            }

            work.AddRange(snapshot.Regions
                .Where(region => region.CompletedFates < region.RequiredFates && region.HasActiveReachableFate)
                .Select(region =>
                {
                    var remaining = region.RequiredFates - region.CompletedFates;
                    var score = region.Priority * 100 + (1000 / Math.Max(1, remaining)) + (100 / Math.Max(1, region.EstimatedMinutesToNextCompletion));
                    return new PlannedWork(region.RegionKey, $"Advance Shared FATE: {region.Name}", score, PlanRisk.Medium,
                        $"{remaining} completion(s) remain for the configured regional target.");
                }));

            return work.OrderByDescending(item => item.Priority).ToArray();
        }
    }
}

namespace EZBuddy.Core.Levequests
{
    using EZBuddy.Core.Planning;

    public enum LeveType
    {
        Craft,
        Gather,
        Combat
    }

    public sealed record LeveOption(
        string Key,
        string Name,
        LeveType Type,
        int AllowanceCost,
        int RewardScore,
        int EstimatedMinutes,
        bool VerifiedExecutor,
        int Priority = 0);

    public sealed record LeveSnapshot(
        int CurrentAllowances,
        int AllowanceCap,
        int PreserveAllowances,
        int BurnWhenAtOrAbove,
        int MaximumLevesThisRun,
        IReadOnlyList<LeveOption> Options);

    public static class LevequestPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(LeveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.CurrentAllowances < 0 || snapshot.AllowanceCap <= 0 || snapshot.PreserveAllowances < 0 ||
                snapshot.BurnWhenAtOrAbove < 0 || snapshot.MaximumLevesThisRun <= 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            if (snapshot.CurrentAllowances < snapshot.BurnWhenAtOrAbove)
                return Array.Empty<PlannedWork>();

            var spendable = Math.Max(0, snapshot.CurrentAllowances - snapshot.PreserveAllowances);
            return snapshot.Options
                .Where(option => option.AllowanceCost > 0 && option.AllowanceCost <= spendable)
                .Select(option => new PlannedWork(
                    option.Key,
                    option.Name,
                    option.Priority * 100 + (option.RewardScore * 100 / Math.Max(1, option.EstimatedMinutes)),
                    option.VerifiedExecutor ? PlanRisk.Low : PlanRisk.ManualReview,
                    option.VerifiedExecutor
                        ? $"Allowance pressure is active; this verified {option.Type} leve is eligible for burn-down."
                        : "Leve is attractive but no verified execution provider is registered."))
                .OrderByDescending(item => item.Priority)
                .Take(snapshot.MaximumLevesThisRun)
                .ToArray();
        }
    }
}

namespace EZBuddy.Core.DomanEnclave
{
    using EZBuddy.Core.Planning;

    public sealed record DomanDonationCandidate(
        uint ItemId,
        string Name,
        int QuantityOwned,
        int ReserveQuantity,
        int DonationValueEach,
        bool ExplicitlyApproved);

    public sealed record DomanEnclaveSnapshot(
        int WeeklyRemainingBudget,
        IReadOnlyList<DomanDonationCandidate> Candidates);

    public sealed record DomanDonationPlan(
        IReadOnlyList<(uint ItemId, string Name, int Quantity, int Value)> Donations,
        int TotalValue,
        IReadOnlyList<string> Warnings);

    public static class DomanEnclavePlanner
    {
        public static DomanDonationPlan Build(DomanEnclaveSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.WeeklyRemainingBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            var remaining = snapshot.WeeklyRemainingBudget;
            var donations = new List<(uint, string, int, int)>();
            var warnings = new List<string>();

            foreach (var item in snapshot.Candidates
                         .Where(item => item.ExplicitlyApproved && item.ItemId != 0 && item.DonationValueEach > 0)
                         .OrderByDescending(item => item.DonationValueEach))
            {
                var available = Math.Max(0, item.QuantityOwned - item.ReserveQuantity);
                if (available == 0 || remaining == 0) continue;
                var quantity = Math.Min(available, remaining / item.DonationValueEach);
                if (quantity <= 0) continue;
                var value = checked(quantity * item.DonationValueEach);
                donations.Add((item.ItemId, item.Name, quantity, value));
                remaining -= value;
            }

            if (remaining > 0 && snapshot.Candidates.Any(item => !item.ExplicitlyApproved && item.QuantityOwned > item.ReserveQuantity))
                warnings.Add("Weekly donation budget remains, but EZBuddy will not donate unapproved inventory items.");

            return new DomanDonationPlan(donations, donations.Sum(x => x.Value), warnings);
        }
    }
}

namespace EZBuddy.Core.Voyages
{
    using EZBuddy.Core.Planning;

    public sealed record VoyageVesselSnapshot(
        string VesselKey,
        string Name,
        bool IsDeployed,
        DateTimeOffset? ReturnAtUtc,
        int ConditionPercent,
        int MinimumConditionPercent,
        int RequiredFuel,
        int AvailableFuel,
        int RequiredRepairKits,
        int AvailableRepairKits,
        string? ConfiguredRouteKey,
        bool RouteVerified);

    public static class VoyagePlanner
    {
        public static IReadOnlyList<PlannedWork> Build(IEnumerable<VoyageVesselSnapshot> vessels, DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(vessels);
            var work = new List<PlannedWork>();
            foreach (var vessel in vessels)
            {
                if (vessel.IsDeployed && vessel.ReturnAtUtc is { } returnAt && returnAt > nowUtc)
                {
                    work.Add(new PlannedWork(vessel.VesselKey + ":wait", $"Wait for {vessel.Name}", 50, PlanRisk.Low,
                        $"Vessel returns at {returnAt:u}."));
                    continue;
                }

                if (vessel.ConditionPercent < vessel.MinimumConditionPercent || vessel.AvailableRepairKits < vessel.RequiredRepairKits)
                {
                    work.Add(new PlannedWork(vessel.VesselKey + ":repair", $"Repair {vessel.Name}", 900, PlanRisk.ManualReview,
                        "Condition or repair-kit stock is below the configured deployment floor."));
                    continue;
                }

                if (vessel.AvailableFuel < vessel.RequiredFuel)
                {
                    work.Add(new PlannedWork(vessel.VesselKey + ":fuel", $"Refuel {vessel.Name}", 850, PlanRisk.ManualReview,
                        "Insufficient configured fuel is available for the next voyage."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(vessel.ConfiguredRouteKey) || !vessel.RouteVerified)
                {
                    work.Add(new PlannedWork(vessel.VesselKey + ":route", $"Review route for {vessel.Name}", 800, PlanRisk.ManualReview,
                        "No verified voyage route is configured."));
                    continue;
                }

                work.Add(new PlannedWork(vessel.VesselKey + ":deploy", $"Deploy {vessel.Name}", 700, PlanRisk.Medium,
                    $"Verified route '{vessel.ConfiguredRouteKey}' is ready and resource floors are satisfied."));
            }
            return work.OrderByDescending(item => item.Priority).ToArray();
        }
    }
}

namespace EZBuddy.Core.IslandSanctuary
{
    using EZBuddy.Core.Planning;

    public sealed record IslandSnapshot(
        int FreeInventorySlots,
        int MinimumFreeInventorySlots,
        int ReadyCrops,
        int EmptyCropPlots,
        int ReadyPastureAnimals,
        int EmptyPastureSlots,
        int GranariesReady,
        int WorkshopDaysUnscheduled,
        bool WorkshopPlanVerified,
        bool HasRequiredSeeds,
        bool HasRequiredFeed);

    public static class IslandSanctuaryPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(IslandSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var work = new List<PlannedWork>();
            var inventoryTight = snapshot.FreeInventorySlots <= snapshot.MinimumFreeInventorySlots;

            if (snapshot.ReadyCrops > 0)
                work.Add(new PlannedWork("crops-harvest", "Harvest ready crops", 800,
                    inventoryTight ? PlanRisk.ManualReview : PlanRisk.Low,
                    inventoryTight ? "Inventory is at the safety floor; harvesting may create overflow." : $"{snapshot.ReadyCrops} crop plot(s) are ready."));

            if (snapshot.EmptyCropPlots > 0)
                work.Add(new PlannedWork("crops-plant", "Plant empty crop plots", 600,
                    snapshot.HasRequiredSeeds ? PlanRisk.Low : PlanRisk.ManualReview,
                    snapshot.HasRequiredSeeds ? $"{snapshot.EmptyCropPlots} empty crop plot(s) can be replanted." : "Required seed stock is missing."));

            if (snapshot.ReadyPastureAnimals > 0)
                work.Add(new PlannedWork("pasture-collect", "Collect pasture leavings", 750,
                    inventoryTight ? PlanRisk.ManualReview : PlanRisk.Low,
                    inventoryTight ? "Inventory is at the safety floor." : $"{snapshot.ReadyPastureAnimals} pasture animal(s) have collectable leavings."));

            if (snapshot.EmptyPastureSlots > 0 && !snapshot.HasRequiredFeed)
                work.Add(new PlannedWork("pasture-feed", "Restore pasture feed stock", 500, PlanRisk.ManualReview,
                    "Pasture capacity is available but required feed stock is missing."));

            if (snapshot.GranariesReady > 0)
                work.Add(new PlannedWork("granary", "Collect and redeploy granaries", 700, PlanRisk.Low,
                    $"{snapshot.GranariesReady} granary expedition(s) are ready."));

            if (snapshot.WorkshopDaysUnscheduled > 0)
                work.Add(new PlannedWork("workshop", "Schedule island workshop", 650,
                    snapshot.WorkshopPlanVerified ? PlanRisk.Low : PlanRisk.ManualReview,
                    snapshot.WorkshopPlanVerified
                        ? $"{snapshot.WorkshopDaysUnscheduled} unscheduled workshop day(s) can use the verified plan."
                        : "Workshop days are unscheduled, but no verified production plan is configured."));

            return work.OrderByDescending(item => item.Priority).ToArray();
        }
    }
}

namespace EZBuddy.Core.Housing
{
    using EZBuddy.Core.Planning;

    public enum HousingLotteryState
    {
        None,
        EntryAvailable,
        Entered,
        WonClaimRequired,
        LostRefundAvailable
    }

    public sealed record GardenPlotSnapshot(
        string PlotKey,
        string CropName,
        bool Empty,
        bool ReadyToHarvest,
        bool NeedsWater,
        bool CanFertilize,
        bool SeedApproved,
        bool HarvestApproved);

    public sealed record HousingSnapshot(
        HousingLotteryState LotteryState,
        DateTimeOffset? LotteryDeadlineUtc,
        IReadOnlyList<GardenPlotSnapshot> GardenPlots);

    public static class HousingGardeningPlanner
    {
        public static IReadOnlyList<PlannedWork> Build(HousingSnapshot snapshot, DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var work = new List<PlannedWork>();

            switch (snapshot.LotteryState)
            {
                case HousingLotteryState.EntryAvailable:
                    work.Add(new PlannedWork("lottery-entry", "Review housing lottery entry", 1000, PlanRisk.ManualReview,
                        snapshot.LotteryDeadlineUtc is { } deadline ? $"Entry is available until {deadline:u}; purchasing remains a user-reviewed action." : "Lottery entry is available; purchasing remains user-reviewed."));
                    break;
                case HousingLotteryState.WonClaimRequired:
                    work.Add(new PlannedWork("lottery-claim", "Claim won housing plot", 1000, PlanRisk.ManualReview,
                        "A won plot requires user-reviewed claim handling before the deadline."));
                    break;
                case HousingLotteryState.LostRefundAvailable:
                    work.Add(new PlannedWork("lottery-refund", "Retrieve housing lottery refund", 900, PlanRisk.Low,
                        "A refund is available from a completed unsuccessful lottery entry."));
                    break;
            }

            foreach (var plot in snapshot.GardenPlots)
            {
                if (plot.ReadyToHarvest)
                    work.Add(new PlannedWork(plot.PlotKey + ":harvest", $"Harvest {plot.CropName}", 800,
                        plot.HarvestApproved ? PlanRisk.Low : PlanRisk.ManualReview,
                        plot.HarvestApproved ? "Crop is ready and harvesting is explicitly approved." : "Crop is ready, but harvesting is not approved."));
                else if (plot.NeedsWater)
                    work.Add(new PlannedWork(plot.PlotKey + ":water", $"Water {plot.CropName}", 750, PlanRisk.Low, "Crop needs watering."));

                if (plot.Empty)
                    work.Add(new PlannedWork(plot.PlotKey + ":plant", "Plant empty garden plot", 600,
                        plot.SeedApproved ? PlanRisk.Low : PlanRisk.ManualReview,
                        plot.SeedApproved ? "An approved seed plan exists for this plot." : "Plot is empty, but no approved seed is configured."));
                else if (plot.CanFertilize)
                    work.Add(new PlannedWork(plot.PlotKey + ":fertilize", $"Fertilize {plot.CropName}", 300, PlanRisk.Low, "Fertilization is available as an optional maintenance action."));
            }

            return work.OrderByDescending(item => item.Priority).ToArray();
        }
    }
}
