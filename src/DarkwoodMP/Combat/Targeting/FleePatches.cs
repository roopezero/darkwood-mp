using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: a stalker told to flee (stalkerRunaway_onAttack starts setToFlee)
    /// runs from the player it was chasing.
    /// </summary>
    [HarmonyPatch(typeof(Character), "setToFlee")]
    static class FleeFromTargetPatch
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
    static class RunAwayAfterAttackPatch
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
    static class RunAwayActivityPatch
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
    static class FleeFromAnyPlayerPatch
    {
        private static readonly Collider[] _near = new Collider[256];

        static bool Prefix(Character __instance)
        {
            if (!Threat.Enabled)
            {
                return true;
            }

            var c = __instance;
            if (c.summonsAfterEscaping)
            {
                return false;
            }

            if (c.wantToDespawn)
            {
                c.runAway(Threat.FleeFrom(c));
                return false;
            }

            if (c.behaviour != Character.Behaviour.escaping)
            {
                return false;
            }

            Vector3 me = c.transform.position;
            // the game's radius and layers
            int n = Physics.OverlapSphereNonAlloc(me, 500f, _near, 2099200);
            CharBase nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                CharBase cb = null;
                if (_near[i] != null)
                {
                    cb = _near[i].GetComponent<CharBase>();
                }

                if (!Proxies.IsPlayer(cb)
                    || (cb is NetPlayerProxy && !cb.alive))
                {
                    continue;
                }

                float d = Core.trueDistance(cb.transform.position, me);
                if (d < best)
                {
                    best = d;
                    nearest = cb;
                }
            }

            if (nearest != null)
            {
                c.runAway(nearest.transform.position);
            }

            return false;
        }
    }
}
