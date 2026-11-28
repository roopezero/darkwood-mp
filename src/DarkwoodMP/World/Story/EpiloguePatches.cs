using HarmonyLib;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The epilogue's patches (see EpilogueSync). Each does nothing with no
    /// session, so playing alone is unaffected.
    /// </summary>
    [HarmonyPatch(typeof(EpilogueOutcomes), "goToCredits")]
    static class EpilogueOutcomesGoToCredits
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.BeforeCredits();
            }
        }
    }

    /// <summary>
    /// Petting the flat's dog holds our player still until its clip ends;
    /// it doesn't start without the clip.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.special_petDog))]
    static class PlayerSpecialPetDog
    {
        static bool Prefix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Epilogue.MayPetDog(__instance);
        }
    }

    /// <summary>
    /// The game's pan over the burning forest waits for the others.
    /// </summary>
    [HarmonyPatch(
        typeof(Events), nameof(Events.fireWorldEvent),
        new[] { typeof(string) })]
    static class EventsFireWorldEventPan
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(string type)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Epilogue.WorldEventFired(type);
        }
    }

    /// <summary>
    /// Our player set the Being on fire: the forest burns for everyone.
    /// </summary>
    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class GameEventsFireBurn
    {
        static void Postfix(GameEvents __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.EventsFired(__instance);
            }
        }
    }

    /// <summary>
    /// Our player crawled to their death in the burning forest.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.die))]
    static class PlayerDieCrawl
    {
        static void Postfix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.AfterDie(__instance);
            }
        }
    }

    /// <summary>
    /// The outcomes screen: the host's are used everywhere.
    /// </summary>
    [HarmonyPatch(typeof(EpilogueOutcomes), "Start")]
    static class EpilogueOutcomesStart
    {
        static void Prefix(EpilogueOutcomes __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.OutcomesStart(__instance);
            }
        }
    }

    /// <summary>
    /// The epilogue's parts: a joiner starts each one beside the others, not on
    /// top of them.
    /// </summary>
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.startDreaming))]
    static class DreamsStartDreamingPart
    {
        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.PartStarted();
            }
        }
    }
}
