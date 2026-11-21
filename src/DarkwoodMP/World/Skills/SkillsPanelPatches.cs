using HarmonyLib;
using RW = Rewired;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game's mouse actions read as not pressed while they are pressed on
    /// the Skills panel (SkillsPanel.EatsAction).
    /// </summary>
    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButtonDown),
        new[] { typeof(string) })]
    internal static class SkillsPanelMouse
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, true))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(RW.Player), nameof(RW.Player.GetButton),
        new[] { typeof(string) })]
    internal static class SkillsPanelMouseHeld
    {
        private static bool Prefix(string actionName, ref bool __result)
        {
            if (!SkillsPanel.EatsAction(actionName, false))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Escape with the Skills panel open closes the panel, not the backpack or
    /// into the pause menu.
    /// </summary>
    [HarmonyPatch(typeof(InputScript), nameof(InputScript.onPressEsc))]
    internal static class SkillsPanelEscape
    {
        private static bool Prefix()
        {
            if (!SkillsPanel.Open || Core.forbidInputs)
            {
                return true; // a film to skip goes first
            }

            SkillsPanel.CloseFromEscape();
            return false;
        }
    }
}
