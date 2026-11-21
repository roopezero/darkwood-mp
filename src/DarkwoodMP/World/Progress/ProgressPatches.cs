using HarmonyLib;

namespace DarkwoodMP.World.Progress
{
    // The shared progression's patches. They run with no session too:
    // Progression lives as long as the game (ModHost.Progress) and keeps
    // the stove's level and reset working playing alone.

    /// <summary>
    /// The stove's full bar gives the level and its point, not the game's
    /// skill choice (Progression.ShowHideTiers).
    /// </summary>
    [HarmonyPatch(typeof(SkillsMenu), nameof(SkillsMenu.showHideTiers))]
    static class SkillsMenuShowHideTiers
    {
        static bool Prefix(SkillsMenu __instance, bool onlyShow)
        {
            Progression progress = ProgressOf.Game();
            if (progress == null)
            {
                return true;
            }

            return progress.ShowHideTiers(__instance, onlyShow);
        }
    }

    /// Cooking essence at a stove adds to the shared essence.
    [HarmonyPatch(typeof(InvItemClass), nameof(InvItemClass.consumeExp))]
    static class InvItemClassConsumeExp
    {
        static void Prefix(out int __state)
        {
            __state = Progression.Experience();
        }

        static void Postfix(int __state)
        {
            Progression progress = ProgressOf.Game();
            if (progress != null)
            {
                progress.ConsumedExp(__state);
            }
        }
    }

    /// The game saved: the host writes the shared record beside it.
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class SaveManagerSaveProgress
    {
        static void Postfix()
        {
            Progression progress = ProgressOf.Game();
            if (progress != null)
            {
                progress.OnHostSaved();
            }
        }
    }

    /// The game's Progression (ModHost.Progress); null before the plugin
    /// has loaded.
    static class ProgressOf
    {
        public static Progression Game()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null)
            {
                return null;
            }

            return plugin.Host.Progress;
        }
    }
}
