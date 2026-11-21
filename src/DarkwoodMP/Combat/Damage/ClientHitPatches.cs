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
    public static class ClientForwardHitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Character __instance, float Damage, Transform attackerTransform,
            bool canCutInHalf, bool byPlayer, bool canInterrupt, bool normalHit)
        {
            var ne = NetEnemies.Instance;
            if (ne == null || !NetEnemies.ClientMode)
            {
                return true;
            }

            if (!ne.IsPuppet(__instance, out uint id))
            {
                if (NetEnemies.Frozen.Contains(__instance))
                {
                    return false; // a frozen one isn't really there
                }

                // A friendly NPC our player hits: the host's copy takes it, and
                // turns on us there.
                return !HostileNpcs.ClientHit(
                    __instance, Damage, attackerTransform, canCutInHalf,
                    byPlayer, canInterrupt, normalHit);
            }

            // A flame of no damage can still set it alight (its burn goes with
            // the hit).
            if (Damage <= 0f
                && (NetEnemies.HitEffects == null
                    || NetEnemies.HitEffects.Count == 0))
            {
                return false;
            }

            // Something thrown (its impact, explosion or the fire it left) hits
            // the enemy in the host's copy of the throw.
            if (World.ThrownHits.Now())
            {
                return false;
            }

            // Of flames, only our own weapon's are ours to send (Flame calls
            // every hit byPlayer); any other fire burns on the host.
            var flame = World.ThrownHits.CurrentFlame;
            if (flame != null && !EnemyAreaHits.ByLocalPlayer(flame))
            {
                return false;
            }

            // Only what our own player does to it is ours to send. Anything
            // else here (a trap, fire, an explosion) happened on the host too,
            // where it hurt the enemy already.
            bool fromPlayer = byPlayer
                || (attackerTransform != null
                    && attackerTransform.GetComponent<Player>() != null);
            if (!fromPlayer)
            {
                return false;
            }

            ne.ClientHit(
                id, Damage, canCutInHalf, fromPlayer, canInterrupt, normalHit,
                NetEnemies.HitEffects);
            return false;
        }
    }

    /// <summary>
    /// Client: note the weapon's effects while our player's melee hit lands, to
    /// send them with it.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    static class ClientHitEffectsPatch
    {
        static void Prefix(MeleeSensor __instance)
        {
            if (NetEnemies.ClientMode
                && __instance.type == MeleeSensor.MeleeSensorType.player)
            {
                NetEnemies.HitEffects = __instance.effects;
            }
        }

        static void Postfix()
        {
            NetEnemies.HitEffects = null;
            HostileNpcs.EndHit();
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            NetEnemies.HitEffects = null;
            HostileNpcs.EndHit();
            return __exception;
        }
    }

    /// <summary>
    /// Client: effects don't run on the host's enemies here; the host runs them
    /// (they came with the hit). Nor do a weapon's effects on a friendly NPC
    /// whose hit just went to the host (HostileNpcs.EffectsForwarded).
    /// </summary>
    [HarmonyPatch]
    static class ClientNoPuppetEffectsPatch
    {
        // Every overload: weapons use the InvItemEffect one, fire and others
        // the ones taking an effect type.
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(
                m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance)
        {
            if (!NetEnemies.ClientMode || NetEnemies.Instance == null)
            {
                return true;
            }

            var c = __instance.GetComponent<Character>();
            return c == null
                || (!NetEnemies.Instance.IsPuppet(c, out _)
                    && !NetEnemies.Frozen.Contains(c)
                    && !HostileNpcs.EffectsForwarded(c));
        }
    }
}
