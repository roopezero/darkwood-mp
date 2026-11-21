using HarmonyLib;

namespace DarkwoodMP.World.Story
{
    /// A joiner's game doesn't build a chapter itself; the host's tells the
    /// joiners (Chapters.BeforeGenerate). High priority: this prefix ran
    /// before WorldTeardown's on the same method when both were in the
    /// World folder, and a type in a subfolder now comes after it in the
    /// DLL, which sets the order of patches of the same priority.
    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class ControllerGenerateChapterStory
    {
        [HarmonyPriority(Priority.High)]
        static bool Prefix(int _chapterId, bool generateSave)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Chapters.BeforeGenerate(_chapterId, generateSave);
        }
    }

    /// The epilogue's dream on the host ends the game for everyone.
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.prepareDream))]
    static class DreamsPrepareDream
    {
        static void Prefix(string presetName)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.OnPrepareDream(presetName);
            }
        }
    }

    /// The older end screen on the host ends the session.
    [HarmonyPatch(typeof(UI), nameof(UI.showEndGame))]
    static class UIShowEndGame
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Chapters.OnShowEndGame();
            }
        }
    }
}
