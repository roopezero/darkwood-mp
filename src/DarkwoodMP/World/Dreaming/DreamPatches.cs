using HarmonyLib;

namespace DarkwoodMP.World.Dreaming
{
    /// <summary>
    /// A level taken at the stove (SkillsMenu.confirmSkills): a client's
    /// brings no dream of its own, the host's level dream is drawn at once
    /// (DreamSync), and the shared level counts it (Progression, which runs
    /// with no session too, as it does playing alone).
    /// </summary>
    [HarmonyPatch(typeof(SkillsMenu), nameof(SkillsMenu.confirmSkills))]
    static class SkillsMenuConfirmSkills
    {
        static void Prefix(out bool __state)
        {
            __state = false;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __state =
                    coop.Dreaming.Confirming(Singleton<Dreams>.Instance);
            }
        }

        static void Postfix(bool __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.Confirmed(Singleton<Dreams>.Instance, __state);
            }

            Plugin plugin = Plugin.Instance;
            if (plugin != null && plugin.Host != null)
            {
                plugin.Host.Progress.LevelTaken();
            }
        }
    }

    /// The game drew a dream: the host's level dream goes to everyone.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.getPreset))]
    static class DreamsGetPreset
    {
        static void Postfix(DreamPreset __result)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.PresetChosen(__result);
            }
        }
    }

    /// Our dream is about to end: everyone's shared dream ends.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.initiateEndDreaming))]
    static class DreamsInitiateEndDreaming
    {
        static void Prefix(Dreams __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.Ending(__instance);
            }
        }
    }

    /// Our game's dream transition starts: a story dream every player
    /// dreams together is sent at once.
    [HarmonyPatch(typeof(DreamTransition), nameof(DreamTransition.transition))]
    static class DreamTransitionTransition
    {
        static void Prefix(DreamTransition __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.TransitionStarts(__instance);
            }
        }
    }

    /// Our game started dreaming.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.startDreaming))]
    static class DreamsStartDreaming
    {
        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.Started();
            }
        }
    }

    /// Our dream ended: the shared dream is forgotten once it's over.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.endDreaming))]
    static class DreamsEndDreaming
    {
        static void Postfix(Dreams __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Dreaming.AfterEnd(__instance);
            }
        }
    }
}
