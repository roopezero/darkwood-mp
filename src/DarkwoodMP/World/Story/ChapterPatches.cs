using HarmonyLib;

namespace DarkwoodMP.World.Story
{
    [HarmonyPatch(typeof(Controller), nameof(Controller.generateChapter))]
    static class GenerateChapterPatch
    {
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

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.prepareDream))]
    static class EpilogueStartPatch
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

    [HarmonyPatch(typeof(UI), nameof(UI.showEndGame))]
    static class EndScreenPatch
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
