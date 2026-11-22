using DarkwoodMP.World.Breaking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A hit on an item (BreakSync). On a client, a hit the host makes too
    /// (an explosion, fire) isn't made here: the host's ItemHit is.
    /// </summary>
    [HarmonyPatch(
        typeof(Item), nameof(Item.getHit),
        new[] { typeof(int), typeof(Transform), typeof(bool) })]
    static class ItemGetHit
    {
        static bool Prefix(Item __instance, out ItemState __state)
        {
            __state = default;
            if (Coop.Current == null)
            {
                return true;
            }

            return BreakSync.BeforeHit(__instance, out __state);
        }

        static void Postfix(
            Item __instance, int damage, Transform attackerTransform,
            bool normalHit, ItemState __state)
        {
            if (Coop.Current == null)
            {
                return;
            }

            var hit = new BreakSync.Hit
            {
                Attacker = attackerTransform,
                Damage = damage,
                Normal = normalHit,
                Before = __state,
            };

            BreakSync.AfterHit(__instance, hit);
        }
    }

    /// <summary>
    /// Broken by itself (not by a hit, which ItemHit carries, breaking it in
    /// the other games too).
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.die))]
    static class ItemDie
    {
        static void Prefix(Item __instance, out bool __state)
        {
            __state = false;
            if (Coop.Current != null)
            {
                __state = BreakSync.BeforeChange(__instance);
            }
        }

        static void Postfix(Item __instance, bool __state)
        {
            if (Coop.Current != null)
            {
                BreakSync.AfterBreak(__instance, __state);
            }
        }
    }

    /// <summary>
    /// A broken item repaired (Constructible.construct calls setNormalState).
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.setNormalState))]
    static class ItemSetNormalState
    {
        static void Prefix(Item __instance, out bool __state)
        {
            __state = false;
            if (Coop.Current != null)
            {
                __state = BreakSync.BeforeChange(__instance);
            }
        }

        static void Postfix(Item __instance, bool __state)
        {
            if (Coop.Current != null)
            {
                BreakSync.AfterRepair(__instance, __state);
            }
        }
    }

    /// <summary>
    /// An exploding barrel or gas bottle set off in this game
    /// (BreakSync.BeforeExplode).
    /// </summary>
    [HarmonyPatch(
        typeof(Explodes), nameof(Explodes.onActivate), new System.Type[0])]
    static class ExplodesOnActivate
    {
        static void Prefix(Explodes __instance, out BreakSync.Explosion __state)
        {
            __state = default;
            if (Coop.Current != null)
            {
                __state = BreakSync.BeforeExplode(__instance);
            }
        }

        static void Postfix(Explodes __instance, BreakSync.Explosion __state)
        {
            if (Coop.Current != null)
            {
                BreakSync.AfterExplode(__instance, __state);
            }
        }
    }
}
