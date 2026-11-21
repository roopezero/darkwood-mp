using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Doors
{
    // The door leaf's patches (DoorLeaf, DoorPush). They are apart from
    // DoorPatches' patches on the same methods, which send the calls.

    /// <summary>
    /// An open door's leaf is saved as it's loaded (DoorLeaf.Saving). Also
    /// with no session: the save is the player's.
    /// </summary>
    [HarmonyPatch(
        typeof(Door.SaveState), MethodType.Constructor, new[] { typeof(Door) })]
    static class DoorSaveStateCtor
    {
        static void Postfix(Door.SaveState __instance, Door door)
        {
            DoorLeaf.Saving(__instance, door);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorOpenLeaf
    {
        static void Postfix(
            Door __instance, Vector3 openerPosition, Transform openerTransform)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                var at = Doors.Opener(openerPosition, openerTransform);
                coop.Pushes.Opened(__instance, at);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.close))]
    static class DoorCloseLeaf
    {
        static void Postfix(Door __instance)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Leaves.Closed(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Door), "OnEnable")]
    static class DoorOnEnable
    {
        static void Postfix(Door __instance)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Leaves.Enabled(__instance);
            }
        }
    }
}
