using HarmonyLib;

namespace DarkwoodMP.World.Talk
{
    /// <summary>
    /// Our player talks to an NPC: the host may refuse it while a joiner
    /// talks to them, and an opened dialogue takes the NPC (TalkLock).
    /// </summary>
    [HarmonyPatch(typeof(NPC), nameof(NPC.talkTo))]
    static class NpcTalkTo
    {
        static bool Prefix(NPC __instance) =>
            TalkLock.BeforeTalkTo(__instance);

        static void Postfix(NPC __instance) =>
            TalkLock.AfterTalkTo(__instance);
    }

    /// <summary>
    /// Our dialogue closed: let go of the NPC (TalkLock). Named apart from
    /// DialoguePatches' DialogueWindowClose, which patches the same method.
    /// </summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.close))]
    static class DialogueWindowCloseTalk
    {
        static void Postfix(DialogueWindow __instance) =>
            TalkLock.AfterClose(__instance);
    }
}
