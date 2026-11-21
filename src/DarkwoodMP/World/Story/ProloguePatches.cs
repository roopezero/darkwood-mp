using HarmonyLib;

namespace DarkwoodMP.World.Story
{
    [HarmonyPatch(
        typeof(Dreams.SaveState), MethodType.Constructor,
        new[] { typeof(Dreams) })]
    static class PrologueSnapshotPatch
    {
        static void Postfix(Dreams.SaveState __instance, Dreams dreams)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Prologue.ForSnapshot(__instance, dreams);
            }
        }
    }

    [HarmonyPatch(
        typeof(OutsideLocations.SaveState), MethodType.Constructor,
        new[] { typeof(OutsideLocations) })]
    static class PrologueSnapshotPlacePatch
    {
        static void Postfix(OutsideLocations.SaveState __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Prologue.ForSnapshot(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.endDreaming))]
    static class PrologueEndPatch
    {
        static void Prefix(Dreams __instance, out string __state)
        {
            if (__instance.dreaming && __instance.preset != null)
            {
                __state = Core.getTrueLocationName(__instance.preset.name);
            }
            else
            {
                __state = null;
            }
        }

        static void Postfix(Dreams __instance, string __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Prologue.AfterEnd(__instance, __state);
            }
        }
    }
}
