using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Combat.Joiners;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: an enemy bumping into a joiner's proxy does what it does when it
    /// bumps into the host's player (Character.onCollideWith only checks for a
    /// Player): a banshee goes off, a sleeping enemy wakes, others react to
    /// them. The rest of the game's method (doors, water and so on) still runs,
    /// as it does after a player bump.
    /// </summary>
    [HarmonyPatch(typeof(Character), "onCollideWith")]
    static class CharacterOnCollideWith
    {
        static bool Prefix(Character __instance, Collider _collider)
        {
            return JoinerAi.Bumped(__instance, _collider);
        }
    }

    /// <summary>
    /// Host: an enemy hit or bumped by a joiner reacts as it does to the host's
    /// player: it chases and attacks them, instead of rolling its cowardice.
    /// The transpiler swaps reactToCharacter's GetComponent&lt;Player&gt;()
    /// test for one that also accepts a proxy, and its attackPlayer() for an
    /// attack on whoever it reacted to.
    /// </summary>
    [HarmonyPatch(typeof(Character), "reactToCharacter")]
    static class CharacterReactToCharacter
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return JoinerAi.SwapReact(instructions);
        }
    }

    /// <summary>
    /// Host: an enemy that left its location doesn't jump back to its spawn
    /// point while a joiner is near it (inSightOrCloseToPlayer only looks at
    /// the host's player). Line of sight to a proxy isn't checked, so a joiner
    /// counts within 1500, more than the game's 1000 for the host's player.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.inSightOrCloseToPlayer))]
    static class CharacterInSightOrCloseToPlayer
    {
        static void Postfix(Character __instance, ref bool __result)
        {
            JoinerAi.NearJoiner(__instance, ref __result);
        }
    }

    /// <summary>
    /// Host: the forest spirit leaves when the player it chases goes indoors,
    /// not whenever the host does.
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkStuff")]
    static class CharacterCheckStuff
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(
                instructions, JoinerAi.HostField(
                    AccessTools.Field(
                        typeof(CharBase), nameof(CharBase.isInside))),
                AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.TargetIsInside)),
                "Character.checkStuff (forest spirit)");
    }

    /// <summary>
    /// Host: a defensive enemy that wakes up paths toward the player it has the
    /// most threat for, not always the host.
    /// </summary>
    [HarmonyPatch(typeof(Character), "OnAniFinish")]
    static class CharacterOnAniFinish
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(
                instructions, JoinerAi.HostThen(
                    AccessTools.PropertyGetter(
                        typeof(Component), nameof(Component.transform))),
                AccessTools.Method(
                    typeof(Threat), nameof(Threat.PursuitTarget)),
                "Character.OnAniFinish");
    }

    /// <summary>
    /// Host: a hatching spider joins the location it is in when a joiner, not
    /// the host, is the nearest player.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.onSpiderSpawn))]
    static class CharacterOnSpiderSpawn
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(
                instructions, JoinerAi.HostBigLocation,
                AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.HatchLocation)),
                "Character.onSpiderSpawn");
    }

    /// <summary>
    /// Host: a spawned character's activities (going to a location's doors,
    /// windows and so on, or to "the player") use the hideout of the joiner it
    /// was sent at, or the location of the player it's after, not the host's.
    /// </summary>
    [HarmonyPatch(
        typeof(Character.Activity), nameof(Character.Activity.assignTarget))]
    static class ActivityAssignTarget
    {
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var l = JoinerAi.Swap(
                instructions, JoinerAi.HostBigLocation, AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.SpawnedLocation)),
                "Character.Activity.assignTarget (location)", expected: 2);
            return JoinerAi.Swap(
                l, JoinerAi.HostThen(
                    AccessTools.PropertyGetter(
                        typeof(Component), nameof(Component.gameObject))),
                AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.ActivityPlayer)),
                "Character.Activity.assignTarget (player)");
        }
    }

    /// <summary>
    /// Host: an enemy chasing a joiner into a location starts at the waypoint
    /// nearest them, as checkOutLocation does for the host's player when it's
    /// inside. The prefix notes the joiner; gotoNextWaypoint then gets the
    /// waypoint (JoinerChases).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.checkOutLocation))]
    static class CharacterCheckOutLocation
    {
        static void Prefix(Character __instance)
        {
            Plugin.Instance.Host.Chases.Note(__instance);
        }

        static Exception Finalizer(Exception __exception)
        {
            Plugin.Instance.Host.Chases.Forget();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.gotoNextWaypoint))]
    static class CharacterGotoNextWaypoint
    {
        static void Prefix(Character __instance, ref Waypoint _waypoint)
        {
            Plugin.Instance.Host.Chases.Waypoint(__instance, ref _waypoint);
        }
    }

    /// <summary>
    /// Host: the scripted brother's weak attack goes by the health of the
    /// player it is fighting.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.checkBrotherWeakAttack))]
    static class CharacterCheckBrotherWeakAttack
    {
        static bool Prefix(Character __instance)
        {
            return JoinerAi.BrotherWeakAttack(__instance);
        }
    }
}
