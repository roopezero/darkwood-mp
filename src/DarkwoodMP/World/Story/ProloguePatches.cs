using HarmonyLib;

namespace DarkwoodMP.World.Story
{
    /// The host's snapshot saved in a stage goes into that stage as the
    /// joiner loads it (Prologue.ForSnapshot).
    [HarmonyPatch(
        typeof(Dreams.SaveState), MethodType.Constructor,
        new[] { typeof(Dreams) })]
    static class DreamsSaveStateCtor
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

    /// The host's snapshot saved in a stage has the player not yet in the
    /// stage's place (Prologue.ForSnapshot).
    [HarmonyPatch(
        typeof(OutsideLocations.SaveState), MethodType.Constructor,
        new[] { typeof(OutsideLocations) })]
    static class OutsideLocationsSaveStateCtor
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

    /// The prologue's end puts a joiner beside the hideout's start
    /// (Prologue.AfterEnd). The dream that ends is read before it ends.
    /// High priority on the postfix: it ran before Dreaming's
    /// DreamsEndDreaming when this was in the World folder, and a type in
    /// World/Story now comes after World/Dreaming in the DLL, which sets
    /// the order of patches of the same priority.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.endDreaming))]
    static class DreamsEndDreamingPrologue
    {
        static void Prefix(Dreams __instance, out string __state)
        {
            __state = null;
            if (Coop.Current != null)
            {
                __state = Prologue.Ending(__instance);
            }
        }

        [HarmonyPriority(Priority.High)]
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
