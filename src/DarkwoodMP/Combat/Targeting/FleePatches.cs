using System.Collections.Generic;
using HarmonyLib;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: a stalker told to flee (stalkerRunaway_onAttack starts setToFlee)
    /// runs from the player it was chasing.
    /// </summary>
    [HarmonyPatch(typeof(Character), "setToFlee")]
    static class CharacterSetToFlee
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(
                instructions, PlayerPositionTranspiler.Flee,
                "Character.setToFlee");
    }

    /// <summary>
    /// Host: an enemy that runs away after an attack (runAwayAfterAttacking)
    /// runs from the player it attacked.
    /// </summary>
    [HarmonyPatch(
        typeof(Character), nameof(Character.attacking), MethodType.Setter)]
    static class CharacterAttackingSetter
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(
                instructions, PlayerPositionTranspiler.Flee,
                "Character.attacking (setter)");
    }

    /// <summary>
    /// Host: a scripted runAway activity with no target
    /// (Character.Activity.run: "run from the player") runs from the player the
    /// character is after, or the nearest one, not always from the host's.
    /// </summary>
    [HarmonyPatch(typeof(Character.Activity), nameof(Character.Activity.run))]
    static class ActivityRun
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            PlayerPositionTranspiler.Swap(
                instructions, PlayerPositionTranspiler.Flee,
                "Character.Activity.run", characterArg: 1);
    }

    /// <summary>
    /// Host, with other players in the world: an escaping enemy runs from
    /// whichever player comes within 500 of it (the game checks only the
    /// host's), and one on its way out runs from the player it was after or the
    /// nearest. Otherwise the game's own method runs.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkIfBeingChased")]
    static class CharacterCheckIfBeingChased
    {
        static bool Prefix(Character __instance) =>
            Plugin.Instance.Host.Threats.ChecksChase(__instance);
    }
}
