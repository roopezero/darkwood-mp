using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: damage to a host's enemy is the host's to apply.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.getHit),
        new[]
        {
            typeof(float),
            typeof(Transform),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    public static class CharacterGetHitFromClient
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Character __instance, float Damage, Transform attackerTransform,
            bool canCutInHalf, bool byPlayer, bool canInterrupt, bool normalHit)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Enemies.TakesHit(__instance, new GameHit
            {
                Damage = Damage,
                Attacker = attackerTransform,
                CanCutInHalf = canCutInHalf,
                ByPlayer = byPlayer,
                CanInterrupt = canInterrupt,
                NormalHit = normalHit,
            });
        }
    }

    /// <summary>
    /// Client: note the weapon's effects while our player's melee hit lands, to
    /// send them with it.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    static class MeleeSensorNotesEffects
    {
        static void Prefix(MeleeSensor __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.MeleeTouches(__instance);
            }
        }

        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.MeleeTouched();
            }
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.MeleeTouched();
            }

            return __exception;
        }
    }

    /// <summary>
    /// Client: effects don't run on the host's enemies here; the host runs them
    /// (they came with the hit). Nor do a weapon's effects on a friendly NPC
    /// whose hit just went to the host (HostileNpcs.EffectsForwarded).
    /// </summary>
    [HarmonyPatch]
    static class CharacterEffectsActivateOnCopy
    {
        // Every overload: weapons use the InvItemEffect one, fire and others
        // the ones taking an effect type.
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(
                m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Enemies.RunsEffects(__instance);
        }
    }
}
