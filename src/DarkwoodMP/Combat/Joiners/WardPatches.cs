using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Combat.Beams;
using DarkwoodMP.Combat.Joiners;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The ward checks read the enemy's target, and an enemy afraid of a ward
    /// runs from the player it was after (who has it), not from the host's.
    /// </summary>
    [HarmonyPatch(typeof(Character), "canSeeEnemy")]
    static class CharacterCanSeeEnemy
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return PlayerPositionTranspiler.Swap(
                Wards.SwapEffects(instructions),
                PlayerPositionTranspiler.Flee,
                "Character.canSeeEnemy");
        }
    }

    [HarmonyPatch(typeof(Character), "processAnims")]
    static class CharacterProcessAnims
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
                Wards.SwapForestSkills(instructions, "Character.processAnims");
    }

    [HarmonyPatch(typeof(Character), "onSeeEnemyNear")]
    static class CharacterOnSeeEnemyNear
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
                Wards.SwapForestSkills(
                    instructions, "Character.onSeeEnemyNear");
    }
}
