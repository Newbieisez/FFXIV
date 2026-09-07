namespace EZBuddy.Core.Research;

public sealed partial class RouteReferenceScanner
{
    static RouteReferenceScanner()
    {
        AddKeys(NpcKeys,
            "bossnpcid", "boss_npc_id",
            "targetnpcid", "target_npc_id",
            "enemyid", "enemy_id",
            "enemynpcbaseid", "enemy_npc_base_id");

        AddKeys(ObjectKeys,
            "targetobjectid", "target_object_id",
            "doorid", "door_id",
            "switchid", "switch_id",
            "liftid", "lift_id",
            "chestid", "chest_id",
            "cofferid", "coffer_id");

        AddKeys(ActionKeys,
            "bossactionid", "boss_action_id",
            "boss spell id", "mechanicactionid", "mechanic_action_id");
    }

    private static void AddKeys(HashSet<string> target, params string[] keys)
    {
        foreach (var key in keys)
        {
            target.Add(NormalizeKey(key));
        }
    }
}
