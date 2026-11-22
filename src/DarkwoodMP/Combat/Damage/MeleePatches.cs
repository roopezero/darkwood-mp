using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// An enemy's attack is a short-lived MeleeSensor trigger; the game applies
    /// it to a Character or a Player it touches. A proxy is neither, so its
    /// hits are handled here: the same checks the game makes for a player (not
    /// ignored, the attacker can see it), then the damage and effects go to the
    /// proxy's player.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    public static class MeleeSensorHitsStandIn
    {
        private static bool Prefix(
            MeleeSensor __instance,
            Collider _collider) =>
            StandInMelee.Touches(__instance, _collider);
    }
}
