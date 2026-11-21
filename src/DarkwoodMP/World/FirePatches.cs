using System.Collections;
using HarmonyLib;

namespace DarkwoodMP.World
{
    [HarmonyPatch(typeof(Burn), "Start")]
    static class BurnStart
    {
        static void Postfix(Burn __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fire.Started(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Burn), nameof(Burn.stop))]
    static class BurnStop
    {
        static void Prefix(Burn __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fire.Stopped(__instance);
            }
        }
    }

    /// <summary>
    /// Client: fire hurts only our own player here; the rest burns on the host.
    /// </summary>
    [HarmonyPatch(typeof(Burn), nameof(Burn.waitToGetHit))]
    static class BurnWaitToGetHit
    {
        static bool Prefix(Burn __instance, ref IEnumerator __result)
        {
            if (FireSync.Hurts(__instance))
            {
                return true;
            }

            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
