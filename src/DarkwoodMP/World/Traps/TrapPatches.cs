using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    // The trap patches (TrapSync, DisarmAnimation). Each does nothing with
    // no session: playing alone is the game's own.

    /// <summary>A trap going off (TrapSync).</summary>
    [HarmonyPatch(typeof(Trigger), "OnAfterTrigger")]
    static class OnAfterTrigger
    {
        static void Prefix(
            Trigger __instance, Collider other, bool doConnectChain,
            out TrapSync.Held __state)
        {
            __state = null;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Traps.BeforeTrigger(
                __instance, other, doConnectChain);
        }

        static void Postfix(
            Trigger __instance, Collider other, TrapSync.Held __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.AfterTrigger(__instance, other, __state);
        }
    }

    /// <summary>
    /// What touches a trap. Client: the player's health is noted before a
    /// bite, so a refused trap can give it back, and traps don't go off on
    /// characters here (TrapSync).
    /// </summary>
    [HarmonyPatch(typeof(Trigger), nameof(Trigger.checkCollision))]
    static class CheckCollision
    {
        static bool Prefix(Collider other)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Traps.BeforeCollision(other);
        }

        static void Postfix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.AfterCollision();
        }
    }

    /// <summary>
    /// A trap disarmed (or failed to be): it's taken away, or left sprung,
    /// for everyone.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.switchTriggerState))]
    static class SwitchTriggerState
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.BeforeSwitch();
        }

        static void Postfix(Item __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.AfterSwitch(__instance);
        }
    }

    /// <summary>Co-op's crouch for a disarm (DisarmAnimation).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.doDisarm))]
    static class DoDisarm
    {
        static void Postfix(Player __instance, Item trapToDisarm)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.Disarms.Animation.Crouch(__instance, trapToDisarm);
        }
    }

    /// <summary>
    /// A progress bar completed: a client's disarm of a shared trap goes to
    /// the host (TrapDisarms.AskHost), and a disarm's crouch ends.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.progressBarCompleted))]
    static class ProgressBarCompleted
    {
        static void Prefix(Player __instance, out bool __state)
        {
            __state = __instance.disarmingTrap;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Traps.Disarms.AskHost(__instance);
        }

        static void Postfix(Player __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null || !__state)
            {
                return;
            }

            coop.Traps.Disarms.Animation.StandUp(__instance);
        }
    }

    /// <summary>A disarm interrupted: its crouch ends.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.interruptProgressBar))]
    static class InterruptProgressBar
    {
        static void Prefix(Player __instance, out bool __state)
        {
            __state = __instance.disarmingTrap;
        }

        static void Postfix(Player __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null || !__state)
            {
                return;
            }

            coop.Traps.Disarms.Animation.StandUp(__instance);
        }
    }
}
