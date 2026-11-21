using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Culling
{
    /// <summary>
    /// The world's culling grid refreshing round the host's player:
    /// SharedCulling keeps it entered round every player. It lives on
    /// ModHost, as the test dummy needs it with no session.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.refreshPosition))]
    internal static class WorldGridRefreshPosition
    {
        private static bool Prefix(
            WorldGrid __instance, Vector3 pos, bool instant, bool force)
        {
            ModHost host = Plugin.Instance.Host;
            if (host == null)
            {
                return true;
            }

            var call = new GridRefresh
            {
                Pos = pos,
                Instant = instant,
                Force = force,
            };

            return host.Culling.OnRefresh(__instance, call);
        }
    }
}
