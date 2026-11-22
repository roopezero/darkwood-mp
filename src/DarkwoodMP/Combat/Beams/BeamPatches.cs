using DarkwoodMP.Combat.Beams;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: shadow armour on a copy of the host's enemy
    /// (NetEnemies.SetArmor) or on one of our frozen characters only shows its
    /// effect. Its light check (which would hurt it, set it on our player and
    /// show its health bar) and its damage do nothing: the host's armour takes
    /// them, and the host says when it dies (EnemyArmor). On the host, a
    /// joiner's flashlight counts after the game's check
    /// (JoinerBeams.Checked).
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.checkIfInLight))]
    static class ShadowArmorCheckIfInLight
    {
        static bool Prefix(ShadowArmor __instance)
        {
            return Coop.Current == null || !NetEnemies.IsCopy(__instance);
        }

        static void Postfix(ShadowArmor __instance)
        {
            if (Coop.Current != null)
            {
                JoinerBeams.Checked(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(ShadowArmor), nameof(ShadowArmor.damageMe),
        new[] { typeof(float), typeof(bool) })]
    static class ShadowArmorDamageMe
    {
        static bool Prefix(ShadowArmor __instance)
        {
            return Coop.Current == null || !NetEnemies.IsCopy(__instance);
        }
    }

    /// <summary>
    /// A puppet's flashlight lights the light graph ahead of it
    /// (JoinerBeams.LightBox), also with only the test dummy.
    /// </summary>
    [HarmonyPatch(
        typeof(Light2D), nameof(Light2D.lightGraphNodes),
        new[] { typeof(Bounds) })]
    static class Light2DLightGraphNodes
    {
        static void Prefix(Light2D __instance, ref Bounds _bounds)
        {
            JoinerBeams.LightBox(__instance, ref _bounds);
        }
    }
}
