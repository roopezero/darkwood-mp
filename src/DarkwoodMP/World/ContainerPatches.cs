using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the player places (a trap...) is made in
    /// Player.progressBarCompleted; share it (Containers.PlacingStarts and
    /// PlacingEnds).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.progressBarCompleted))]
    static class ProgressBarCompleted
    {
        static void Prefix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.PlacingStarts(__instance);
        }

        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.PlacingEnds();
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Containers.Placing = false;
            }

            return __exception;
        }
    }

    /// <summary>
    /// Things made while playing together (a dropped item) are shared with the
    /// others (Containers.Made). It makes them with the Object overload,
    /// which shares nothing while this one runs (MakingStarts, MakingEnds).
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(string),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class AddPrefabByPath
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.MakingStarts();
        }

        static void Postfix(string prefab, GameObject __result)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.Made(prefab, __result);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Containers.MakingEnds();
            }

            return __exception;
        }
    }

    /// <summary>
    /// What the player places is made with the Object overload
    /// (Player.progressBarCompleted passes the item's prefab, InvItem.item),
    /// not the string one: shared from here (Containers.MadeFrom), unless the
    /// string overload is the caller.
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class AddPrefabByObject
    {
        static void Postfix(Object prefab, GameObject __result)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.MadeFrom(prefab, __result);
        }
    }

    /// <summary>See Containers.OpenedNow.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.setOpenedItemInventory))]
    static class SetOpenedItemInventory
    {
        static void Postfix(Inventory inv)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Containers.OpenedNow(inv);
        }
    }
}
