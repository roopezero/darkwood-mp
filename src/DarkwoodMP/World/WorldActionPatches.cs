using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A hammer stroke (Player.checkFrameTrigger "HammerWork"): the stroke that
    /// nails up a barricade or rebuilds a broken door calls Door.barricade (or
    /// Window.barricade) first and takes the wood and nails after it. A client
    /// sends that action as a request once the call is over (HammerStrokes).
    /// Does nothing with no session.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.checkFrameTrigger))]
    static class HammerStroke
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Hammer.Enter();
            }
        }

        static void Finalizer()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Hammer.Leave();
            }
        }
    }
}
