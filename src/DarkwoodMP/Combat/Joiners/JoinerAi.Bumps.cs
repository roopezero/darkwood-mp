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
    /// Host: a joiner's stand-in bumping into, hitting or standing near an
    /// enemy, and the brother fighting one, as for the host's player.
    /// </summary>
    static partial class JoinerAi
    {
        private static readonly
            Action<Character, Character, Transform, bool> React =
            AccessTools
                .MethodDelegate<Action<Character, Character, Transform, bool>>(
                    AccessTools.Method(typeof(Character), "reactToCharacter"));

        /// <summary>
        /// Host: an enemy bumping into a joiner's proxy does what it does
        /// when it bumps into the host's player (Character.onCollideWith
        /// only checks for a Player): a banshee goes off, a sleeping enemy
        /// wakes, others react to them. The rest of the game's method
        /// (doors, water and so on) still runs, as it does after a player
        /// bump. False skips the game's method.
        /// </summary>
        public static bool Bumped(Character c, Collider collider)
        {
            var step = JoinerAiRules.Bump(FactsOf(c, collider, out var proxy));
            if (step == BumpStep.Game)
            {
                return true;
            }

            if (!c.touchingColliders.Contains(collider))
            {
                c.touchingColliders.Add(collider);
            }

            if (step == BumpStep.Banshee)
            {
                if (Plugin.Instance.Host.Threats.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[threat] {c.name} (banshee) touched by "
                            + $"{proxy.OwnerName}");
                }

                c.initiateBansheeAttack();
                return false;
            }

            if (step == BumpStep.Wake)
            {
                if (Plugin.Instance.Host.Threats.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[threat] {c.name} woken by {proxy.OwnerName} bumping "
                            + "into it");
                }

                c.wakeup();
                return false;
            }

            if (step == BumpStep.React)
            {
                React(c, null, proxy.transform, false);
            }

            return true;
        }

        private static BumpFacts FactsOf(
            Character c, Collider collider, out NetPlayerProxy proxy)
        {
            proxy = null;
            if (collider == null)
            {
                return default;
            }

            var bump = new BumpFacts
            {
                Collider = true,
                On = Plugin.Instance.Host.Threats.Enabled,
                ClientMode = NetEnemies.ClientMode,
            };

            if (!bump.On || bump.ClientMode)
            {
                return bump;
            }

            bump = bump with { Active = c.isActive };
            if (!bump.Active)
            {
                return bump;
            }

            proxy = collider.GetComponent<NetPlayerProxy>();
            if (proxy == null)
            {
                return bump;
            }

            return bump with
            {
                StandIn = true,
                StandInAlive = proxy.alive,
                StandInAway = proxy.Away,
                StandInInvisible = proxy.invisible,
                Banshee = c.banshee,
                Sleeping = c.sleeping,
                WakesOnlyByHand = c.wakeUpOnlyManually,
                DiesOnContact = c.dieOnContactWithTarget,
            };
        }

        /// <summary>
        /// reactToCharacter: its GetComponent&lt;Player&gt;() test becomes
        /// PlayerOrProxyOn, and its attackPlayer() AttackWhoReacted.
        /// </summary>
        public static IEnumerable<CodeInstruction> SwapReact(
            IEnumerable<CodeInstruction> instructions)
        {
            var attackPlayer = AccessTools.Method(
                typeof(Character), nameof(Character.attackPlayer));
            var list = new List<CodeInstruction>(instructions);
            int test = list.FindIndex(
                ci => ci.operand is MethodInfo m && m.Name == "GetComponent"
                    && m.IsGenericMethod
                    && m.GetGenericArguments()[0]
                        == typeof(Player));
            int call;
            if (test < 0)
            {
                call = -1;
            }
            else
            {
                call = list.FindIndex(test, ci => ci.Calls(attackPlayer));
            }

            if (test < 0 || call < 0)
            {
                Plugin.Log.LogWarning(
                    "ReactToJoinerPatch: no GetComponent<Player>() / "
                        + "attackPlayer() in reactToCharacter (game changed?); "
                        + "a joiner's hit still rolls cowardice");
                return list;
            }

            list[test] = new CodeInstruction(
                OpCodes.Call,
                AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.PlayerOrProxyOn)))
            {
                labels = list[test].labels
            };

            var labels = list[call].labels;
            list[call] = new CodeInstruction(
                OpCodes.Call,
                AccessTools.Method(
                    typeof(JoinerAi), nameof(JoinerAi.AttackWhoReacted)));
            list.Insert(call, new CodeInstruction(OpCodes.Ldarg_2)
            {
                labels = labels
            });

            return list;
        }

        /// <summary>
        /// After inSightOrCloseToPlayer: a living joiner within
        /// JoinerAiRules.NearRange makes it true.
        /// </summary>
        public static void NearJoiner(Character c, ref bool result)
        {
            if (!JoinerAiRules.LooksForJoiners(
                result, Plugin.Instance.Host.Threats.Enabled))
            {
                return;
            }

            Vector3 me = c.transform.position;
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                bool alive = p.alive;
                bool away = p.Away;
                float distance = 0f;
                if (alive && !away)
                {
                    distance = Core.trueDistance(p.transform.position, me);
                }

                if (JoinerAiRules.IsNear(alive, away, distance))
                {
                    result = true;
                    return;
                }
            }
        }

        /// <summary>
        /// checkBrotherWeakAttack while the brother fights a joiner: by the
        /// health of that joiner. False skips the game's method.
        /// </summary>
        public static bool BrotherWeakAttack(Character c)
        {
            var proxy = ProxyOf(c.target);
            if (proxy == null)
            {
                return true;
            }

            if (JoinerAiRules.BrotherWeak(FactsOf(c, proxy)))
            {
                c.attacks[0].disabled = true;
                c.attacks[1].disabled = true;
                c.attacks[2].disabled = false;
            }

            return false;
        }

        private static BrotherFacts FactsOf(
            Character c, NetPlayerProxy proxy)
        {
            var host = Player.Instance;
            float max = 0f;
            if (host != null)
            {
                max = host.maxHealth;
            }

            return new BrotherFacts
            {
                HealthPercent = proxy.HealthPercent,
                HasHost = host != null,
                HostMaxHealth = max,
                Attacks = c.attacks.Count,
            };
        }
    }
}
