using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The player can't take hold of something another player is dragging
    /// (DragSync.MayStart): the game's drag doesn't start.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.startDragging))]
    static class StartDragging
    {
        static bool Prefix(Item __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Drags.MayStart(__instance);
        }
    }
}
