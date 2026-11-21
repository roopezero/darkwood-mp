using HarmonyLib;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// The map opened: the other players' markers go on it. Runs with or
    /// without a session, as the puppets do (ModHost.Players).
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.open))]
    static class MapOpen
    {
        static void Postfix(Map __instance)
        {
            Plugin.Instance.Host.Players.Markers.Opened(__instance);
        }
    }
}
