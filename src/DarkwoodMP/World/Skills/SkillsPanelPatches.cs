using HarmonyLib;
using RW = Rewired;

namespace DarkwoodMP.World
{
    // The Skills panel's patches. The panel works playing alone too, so
    // these run with no session; they do nothing before the mod's state is
    // made (Plugin.Host).

    /// <summary>
    /// The game's mouse actions read as not pressed while they are pressed on
    /// the Skills panel (SkillsWindow.EatsAction).
    /// </summary>
    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButtonDown),
        new[] { typeof(string) })]
    static class RewiredPlayerGetButtonDown
    {
        static bool Prefix(string actionName, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null
                || !plugin.Host.SkillsPanel.EatsAction(actionName, true))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// The same for a mouse action held (SkillsWindow.EatsAction).
    /// </summary>
    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButton),
        new[] { typeof(string) })]
    static class RewiredPlayerGetButton
    {
        static bool Prefix(string actionName, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null
                || !plugin.Host.SkillsPanel.EatsAction(actionName, false))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Escape with the Skills panel open closes the panel, not the backpack or
    /// into the pause menu (SkillsWindow.TakesEscape).
    /// </summary>
    [HarmonyPatch(typeof(InputScript), nameof(InputScript.onPressEsc))]
    static class InputScriptOnPressEsc
    {
        static bool Prefix()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null)
            {
                return true;
            }

            return !plugin.Host.SkillsPanel.TakesEscape();
        }
    }
}
