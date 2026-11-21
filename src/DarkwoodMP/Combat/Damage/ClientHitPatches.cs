using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
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
            var hit = new GameHit
            {
                Damage = Damage,
                Attacker = attackerTransform,
                CanCutInHalf = canCutInHalf,
                ByPlayer = byPlayer,
                CanInterrupt = canInterrupt,
                NormalHit = normalHit,
            };

            var ne = NetEnemies.Instance;
            uint id = 0;
            bool puppet = ne != null && NetEnemies.ClientMode
                && ne.IsPuppet(__instance, out id);
            ClientHitFacts facts = FactsOf(__instance, hit, puppet);
            switch (ClientHitRules.Forward(facts))
            {
                case ClientHit.Game:
                    return true;
                case ClientHit.AskNpcs:
                    // A friendly NPC our player hits: the host's copy takes
                    // it, and turns on us there.
                    return !HostileNpcs.ClientHit(
                        __instance, Damage, attackerTransform, canCutInHalf,
                        byPlayer, canInterrupt, normalHit);
                case ClientHit.ToHost:
                    // Only what our own player does to it is ours to send.
                    ne.ClientHit(
                        id, Damage, canCutInHalf,
                        ClientHitRules.FromPlayer(facts), canInterrupt,
                        normalHit, NetEnemies.HitEffects);
                    return false;
                default:
                    // A frozen copy isn't really there. A flame of no damage
                    // can still set it alight (its burn goes with the hit),
                    // so only a hit of no damage and no effects stops here.
                    // Something thrown hits it in the host's copy of the
                    // throw; of flames only our own weapon's are ours to
                    // send (Flame calls every hit byPlayer); anything else
                    // not by our player (a trap, fire, an explosion)
                    // happened on the host too, where it hurt the enemy
                    // already.
                    return false;
            }
        }

        /// <summary>
        /// The hit as the rule sees it. Read only on a client with
        /// NetEnemies, as the game's getHit runs untouched otherwise.
        /// </summary>
        private static ClientHitFacts FactsOf(
            Character c, in GameHit hit, bool puppet)
        {
            if (NetEnemies.Instance == null || !NetEnemies.ClientMode)
            {
                return new ClientHitFacts { Handled = false };
            }

            int effects = 0;
            if (NetEnemies.HitEffects != null)
            {
                effects = NetEnemies.HitEffects.Count;
            }

            var flame = World.ThrownHits.CurrentFlame;
            bool ours = false;
            if (flame != null)
            {
                ours = EnemyAreaHits.ByLocalPlayer(flame);
            }

            Transform attacker = hit.Attacker;
            return new ClientHitFacts
            {
                Handled = true,
                Puppet = puppet,
                Frozen = NetEnemies.Frozen.Contains(c),
                Damage = hit.Damage,
                Effects = effects,
                Thrown = World.ThrownHits.Now(),
                Flame = flame != null,
                OurFlame = ours,
                ByPlayer = hit.ByPlayer,
                PlayerAttacker = attacker != null
                    && attacker.GetComponent<Player>() != null,
            };
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
            bool player =
                __instance.type == MeleeSensor.MeleeSensorType.player;
            if (ClientHitRules.NotesEffects(NetEnemies.ClientMode, player))
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
    static class CharacterEffectsActivateOnCopy
    {
        // Every overload: weapons use the InvItemEffect one, fire and others
        // the ones taking an effect type.
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(
                m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance)
        {
            return ClientHitRules.RunsEffects(FactsOf(__instance));
        }

        /// <summary>
        /// The effects' character as the rule sees it, read only on a
        /// client with NetEnemies.
        /// </summary>
        static HostEffectsFacts FactsOf(CharacterEffects effects)
        {
            var ne = NetEnemies.Instance;
            if (!NetEnemies.ClientMode || ne == null)
            {
                return new HostEffectsFacts { Handled = false };
            }

            var c = effects.GetComponent<Character>();
            if (c == null)
            {
                return new HostEffectsFacts { Handled = true };
            }

            return new HostEffectsFacts
            {
                Handled = true,
                Character = true,
                Puppet = ne.IsPuppet(c, out _),
                Frozen = NetEnemies.Frozen.Contains(c),
                Forwarded = HostileNpcs.EffectsForwarded(c),
            };
        }
    }
}
