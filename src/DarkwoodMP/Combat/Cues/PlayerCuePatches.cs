using DarkwoodMP.Combat.Cues;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: its player's swing goes to the host too, for the host's enemies
    /// to dodge.
    /// </summary>
    [HarmonyPatch(typeof(Player), "notifyEnemiesAboutAttack")]
    static class PlayerNotifyEnemiesAboutAttack
    {
        static void Postfix()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Cues.Send(CueRules.Dodge);
            }
        }
    }

    /// <summary>
    /// Client: its player's aim goes to the host too. Only Player.aimScare
    /// calls scareInArea.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.scareInArea))]
    static class CharacterScareInArea
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Cues.Send(CueRules.Scare);
            }
        }
    }

    /// <summary>
    /// Client: frozen copies of the host's enemies don't dodge or get scared
    /// (the host's do, and are sent).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.doIWantToDodge))]
    static class CharacterDoIWantToDodge
    {
        static bool Prefix(Character __instance)
        {
            return Coop.Current == null || PlayerCues.Reacts(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.getScared))]
    static class CharacterGetScared
    {
        static bool Prefix(Character __instance)
        {
            return Coop.Current == null || PlayerCues.Reacts(__instance);
        }
    }
}
