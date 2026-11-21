using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A scripted step starts: the host may record a joiner as the recruiter
    /// of the followers it makes (NpcFollowers.Firing).
    /// </summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class GameEventFire
    {
        static void Prefix(GameEvent __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Followers.Firing(__instance);
        }
    }
}
