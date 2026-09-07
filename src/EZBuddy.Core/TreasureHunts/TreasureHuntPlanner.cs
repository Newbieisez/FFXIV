using EZBuddy.Core.Planning;

namespace EZBuddy.Core.TreasureHunts;

public enum TreasureMapState { NoneOwned, Undeciphered, Deciphered, DigLocationKnown, ChestSpawned, PortalAvailable, Complete }

public sealed record TreasureHuntSnapshot(
    TreasureMapState State, uint? MapItemId, bool DecipherCooldownReady, bool HasVerifiedAcquisitionSource,
    bool HasVerifiedTravelTarget, bool InCombat, bool PartyRecommended, int PartySize, bool PortalSupported);

public static class TreasureHuntPlanner
{
    public static IReadOnlyList<PlannedWork> Build(TreasureHuntSnapshot snapshot)
        => snapshot.State switch
        {
            TreasureMapState.NoneOwned =>
                [new("acquire", "Acquire configured treasure map", 700, snapshot.HasVerifiedAcquisitionSource ? PlanRisk.Low : PlanRisk.ManualReview,
                    snapshot.HasVerifiedAcquisitionSource ? "A verified acquisition source is available." : "No verified map acquisition source is configured.")],
            TreasureMapState.Undeciphered when !snapshot.DecipherCooldownReady =>
                [new("cooldown", "Wait for Decipher", 700, PlanRisk.Low, "Decipher cooldown is not ready; do not consume another map.")],
            TreasureMapState.Undeciphered => [new("decipher", "Decipher map", 700, PlanRisk.Low, "Map is owned and Decipher is ready.")],
            TreasureMapState.Deciphered or TreasureMapState.DigLocationKnown =>
                [new("travel-dig", "Travel and Dig", 700, snapshot.HasVerifiedTravelTarget ? PlanRisk.Medium : PlanRisk.ManualReview,
                    snapshot.HasVerifiedTravelTarget ? "A verified target location is available." : "Treasure coordinates require a verified travel target before movement.")],
            TreasureMapState.ChestSpawned when snapshot.InCombat => [new("defend", "Defend treasure chest", 800, PlanRisk.High, "Chest defense encounter is active; combat provider owns execution.")],
            TreasureMapState.ChestSpawned => [new("open", "Open resolved treasure chest", 700, PlanRisk.Medium, "Defense is complete and the chest can be resolved.")],
            TreasureMapState.PortalAvailable when snapshot.PartyRecommended && snapshot.PartySize <= 1 => [new("portal-review", "Portal requires party review", 900, PlanRisk.ManualReview, "Configured policy recommends a party before entering this portal dungeon.")],
            TreasureMapState.PortalAvailable => [new("portal", "Enter treasure portal", 700, snapshot.PortalSupported ? PlanRisk.High : PlanRisk.ManualReview,
                snapshot.PortalSupported ? "Portal route is supported by the configured provider." : "Portal dungeon has no verified EZBuddy provider.")],
            _ => Array.Empty<PlannedWork>()
        };
}
