using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Our player built or repaired something (manual: with the ingredients):
    /// the others build it too (Construction).
    /// </summary>
    [HarmonyPatch(
        typeof(Constructible), nameof(Constructible.construct),
        new[] { typeof(bool), typeof(int) })]
    static class ConstructibleConstruct
    {
        static bool Prefix(
            Constructible __instance, bool manual, int forceOption,
            out bool __state)
        {
            __state = false;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            var call = new Construction.Call
            {
                Manual = manual,
                Force = forceOption,
            };

            return coop.Construction.BeforeBuild(
                __instance, call, out __state);
        }

        static void Postfix(
            Constructible __instance, int forceOption, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Construction.AfterBuild(
                    __instance, forceOption, __state);
            }
        }
    }
}
