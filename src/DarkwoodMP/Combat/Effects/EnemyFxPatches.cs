using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: the game's own count of attackers (in an area of our own)
    /// mustn't turn off the host's combat music (EnemyFx.KeepsMusic).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.checkInCombatChars))]
    static class PlayerCheckInCombatChars
    {
        static bool Prefix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return !coop.EnemyFx.KeepsMusic(__instance);
        }
    }

    /// <summary>
    /// Host: the banshee's scream (FX/BansheeScream where it stands) goes to
    /// the joiners too (EnemyFx.Screamed).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.bansheeScream))]
    static class CharacterBansheeScream
    {
        static void Postfix(Character __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.EnemyFx.Screamed(__instance);
            }
        }
    }

    /// <summary>
    /// Host: pooled effects of enemies (the forest spirit's teleport and spawn)
    /// go to the joiners too (EnemyFx.Pooled).
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPooledPrefab), new[]
    {
        typeof(string),
        typeof(string),
        typeof(Vector3),
        typeof(Quaternion)
    })]
    static class CoreAddPooledPrefab
    {
        // A postfix: it isn't sent when the pool hasn't got it here (the call
        // throws).
        static void Postfix(
            string pool, string prefab, Vector3 position, Transform __result)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.EnemyFx.Pooled(pool, prefab, position, __result);
            }
        }
    }

    /// <summary>
    /// Host: an enemy's animation switched its particle emitter
    /// (EnemyFx.EmitterSwitched).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.modifyEmitter))]
    static class CharacterModifyEmitter
    {
        static void Postfix(Character __instance, int _toggle)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.EnemyFx.EmitterSwitched(__instance, _toggle);
            }
        }
    }
}
