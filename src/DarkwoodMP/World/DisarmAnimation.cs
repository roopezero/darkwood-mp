using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Disarming a trap shows. Some traps make the player crouch to disarm them (Trigger.bendDownToDisarm: the
    /// same animation as searching a container), others only show a progress bar, which is the player's own UI
    /// and isn't seen by the others. In co-op every trap is disarmed crouching, so the others see what you're
    /// doing (they see your character's sprites). The progress bar stays, as before, for you.
    ///
    /// The crouch is the game's own: gettingInventory plays InventoryGet, then InventoryLoop while it lasts,
    /// and hidingInventory plays InventoryHide, at whose end the game lets the player go again.
    /// </summary>
    static class DisarmAnimation
    {
        /// <summary>We made the current disarm crouch (the trap wouldn't have).</summary>
        internal static bool Crouching;

        internal static void StandUp(Player p)
        {
            if (!Crouching) return;
            Crouching = false;
            p.hidingInventory = true;
            p.gotInventory = false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.doDisarm))]
    static class DisarmCrouchPatch
    {
        static void Postfix(Player __instance, Item trapToDisarm)
        {
            DisarmAnimation.Crouching = false;
            if (!CoopPause.InCoop || trapToDisarm == null || !__instance.disarmingTrap) return;
            var trigger = trapToDisarm.GetComponent<Trigger>();
            if (trigger == null || trigger.bendDownToDisarm) return; // already crouches
            __instance.performingAction = true;
            __instance.gettingInventory = true;
            __instance.dodging = false;
            DisarmAnimation.Crouching = true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.progressBarCompleted))]
    static class DisarmDonePatch
    {
        static void Prefix(Player __instance, out bool __state) => __state = __instance.disarmingTrap;
        static void Postfix(Player __instance, bool __state) { if (__state) DisarmAnimation.StandUp(__instance); }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.interruptProgressBar))]
    static class DisarmInterruptedPatch
    {
        static void Prefix(Player __instance, out bool __state) => __state = __instance.disarmingTrap;
        static void Postfix(Player __instance, bool __state) { if (__state) DisarmAnimation.StandUp(__instance); }
    }
}
