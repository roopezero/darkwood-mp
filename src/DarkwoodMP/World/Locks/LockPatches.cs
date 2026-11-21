using DarkwoodMP.World.Locks;
using HarmonyLib;

namespace DarkwoodMP.World
{
    // The lock patches (LockShare). Each does nothing with no session:
    // playing alone is the game's own.

    /// <summary>A lock opened by the story (or by a try below).</summary>
    [HarmonyPatch(typeof(Locked), nameof(Locked.unlock))]
    static class LockedUnlock
    {
        static void Prefix(Locked __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Locks.BeforeUnlock(__instance);
        }

        static void Postfix(Locked __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locks.AfterUnlock(__instance, LockRules.ByStory, __state);
        }
    }

    /// <summary>A lock opened with a key.</summary>
    [HarmonyPatch(typeof(Locked), nameof(Locked.tryToOpenWithKey))]
    static class TryToOpenWithKey
    {
        static void Prefix(Locked __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Locks.BeforeUnlock(__instance);
        }

        static void Postfix(Locked __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locks.AfterUnlock(__instance, LockRules.ByKey, __state);
        }
    }

    /// <summary>A lock opened with a lockpick.</summary>
    [HarmonyPatch(typeof(Locked), nameof(Locked.tryToLockpick))]
    static class TryToLockpick
    {
        static void Prefix(Locked __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Locks.BeforeUnlock(__instance);
        }

        static void Postfix(Locked __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locks.AfterUnlock(__instance, LockRules.ByLockpick, __state);
        }
    }

    /// <summary>
    /// A locked crate picked open in the host's game (LockShare.AfterDisarm).
    /// Not a Depth call: a trap's disarm goes through switchTriggerState,
    /// which must stay the outermost call there.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.disarm))]
    static class Disarm
    {
        static void Prefix(Item __instance, out bool __state) =>
            __state = __instance != null && __instance.searched;

        static void Postfix(Item __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null || __instance == null)
            {
                return;
            }

            coop.Locks.AfterDisarm(__instance, __state);
        }
    }

    /// <summary>
    /// A container opened the game's way (LockShare.AfterOpen). openInventory
    /// uses Player.Instance, so it's always the local player's. A picked
    /// crate is already searched when disarm calls openInventory, so this
    /// sends nothing for it.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.openInventory))]
    static class OpenInventory
    {
        static void Prefix(Item __instance, out bool __state) =>
            __state = __instance != null && __instance.searched;

        static void Postfix(Item __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null || __instance == null)
            {
                return;
            }

            coop.Locks.AfterOpen(__instance, __state);
        }
    }

    /// <summary>
    /// A combination padlock opened (Padlock.unlock, after checkCombination,
    /// or by the story with manually false). The others' copies are unlocked
    /// without the window or events (Padlock.unlock(false)). A client's
    /// player sends the code it entered, and the host checks it and the
    /// reach. The padlock's events (onTryToOpenLocked, onUnlockPadlock) run
    /// in the opener's game, which sends them as scripted events of its own
    /// (ScriptedEvents).
    /// </summary>
    [HarmonyPatch(
        typeof(Padlock), nameof(Padlock.unlock), new[] { typeof(bool) })]
    static class PadlockUnlock
    {
        static void Prefix(Padlock __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Locks.BeforePadlock(__instance);
        }

        static void Postfix(Padlock __instance, bool manually, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locks.AfterPadlock(__instance, manually, __state);
        }
    }
}
