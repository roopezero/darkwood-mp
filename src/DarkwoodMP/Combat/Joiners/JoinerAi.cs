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
    /// Host: places in the game's enemy code that only react to the host's own
    /// player (a Player component, or Player.Instance), made to react to a
    /// joiner's proxy the same way. With nobody else in the world every helper
    /// here gives what the game's own code gives, so single player is
    /// unchanged.
    /// </summary>
    static partial class JoinerAi
    {
        /// <summary>
        /// Swaps each run of instructions that <paramref name="match"/>
        /// recognises (it returns the run's length, or 0) for
        /// "ldarg.0 (or ldarg.1 with <paramref name="arg"/> 1); call helper",
        /// padded with nops, keeping the labels. Logs a warning when it swapped
        /// other than <paramref name="expected"/> runs, or, given
        /// <paramref name="none"/>, that text when it swapped none (the game's
        /// code changed); the method then keeps the game's own behaviour.
        /// </summary>
        public static IEnumerable<CodeInstruction> Swap(
            IEnumerable<CodeInstruction> instructions,
            Func<List<CodeInstruction>, int, int> match,
            MethodInfo helper, string where, int expected = 1, int arg = 0,
            string none = null)
        {
            OpCode load;
            if (arg == 0)
            {
                load = OpCodes.Ldarg_0;
            }
            else
            {
                load = OpCodes.Ldarg_1;
            }

            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int n = match(list, i);
                if (n <= 0)
                {
                    continue;
                }

                list[i] = new CodeInstruction(load)
                {
                    labels = list[i].labels,
                    blocks = list[i].blocks
                };

                list[i + 1] = new CodeInstruction(OpCodes.Call, helper)
                {
                    labels = list[i + 1].labels,
                    blocks = list[i + 1].blocks
                };

                for (int k = 2; k < n; k++)
                {
                    list[i + k] = new CodeInstruction(OpCodes.Nop)
                    {
                        labels = list[i + k].labels,
                        blocks = list[i + k].blocks
                    };
                }

                swapped++;
                i += n - 1;
            }

            if (none != null)
            {
                if (swapped == 0)
                {
                    Plugin.Log.LogWarning(none);
                }
            }
            else if (swapped != expected)
            {
                Plugin.Log.LogWarning(
                    $"JoinerAi: {where}: swapped {swapped} reads of the host's "
                        + $"player, expected {expected} (game changed?)");
            }

            return list;
        }

        public static readonly MethodInfo GetInstance =
            AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));

        /// <summary>Player.Instance.whereAmI.bigLocation</summary>
        public static int HostBigLocation(List<CodeInstruction> l, int i)
        {
            var whereAmI =
                AccessTools.Field(typeof(Player), nameof(Player.whereAmI));
            var big = AccessTools.PropertyGetter(
                typeof(WhereAmI), nameof(WhereAmI.bigLocation));
            if (i + 2 < l.Count && l[i].Calls(GetInstance)
                && l[i + 1].LoadsField(whereAmI) && l[i + 2].Calls(big))
            {
                return 3;
            }

            return 0;
        }

        /// <summary>Player.Instance.&lt;getter&gt;</summary>
        public static Func<List<CodeInstruction>, int, int> HostThen(
            MethodInfo getter) =>
            (l, i) =>
            {
                if (i + 1 < l.Count && l[i].Calls(GetInstance)
                    && l[i + 1].Calls(getter))
                {
                    return 2;
                }

                return 0;
            };

        /// <summary>Player.Instance.&lt;field&gt;</summary>
        public static Func<List<CodeInstruction>, int, int> HostField(
            FieldInfo field) =>
            (l, i) =>
            {
                if (i + 1 < l.Count && l[i].Calls(GetInstance)
                    && l[i + 1].LoadsField(field))
                {
                    return 2;
                }

                return 0;
            };

        public static NetPlayerProxy ProxyOf(Transform t)
        {
            if (t != null && Threat.Enabled)
            {
                return t.GetComponent<NetPlayerProxy>();
            }

            return null;
        }

        /// <summary>
        /// Whether a position is on indoor ground, the way CharBase.checkGround
        /// decides isInside.
        /// </summary>
        public static bool InsideAt(Vector3 pos)
        {
            if (!Physics.Raycast(
                pos + new Vector3(0f, 300f, 0f), Vector3.down, out var hit,
                1000f, 2))
            {
                return false;
            }

            Ground g = null;
            if (hit.collider != null)
            {
                g = hit.collider.GetComponent<Ground>();
            }

            return g != null && g.isInside;
        }

        // ---- helpers the transpilers call ----

        /// <summary>
        /// The forest spirit leaves when its player goes indoors: the joiner it
        /// chases, or else the host's.
        /// </summary>
        public static bool TargetIsInside(Character c)
        {
            Transform t = null;
            if (c != null)
            {
                t = c.target;
            }

            var proxy = ProxyOf(t);
            if (proxy != null)
            {
                return JoinerAiRules.TargetInside(
                    true, InsideAt(proxy.transform.position), false);
            }

            return JoinerAiRules.TargetInside(
                false, false, Player.Instance.isInside);
        }

        /// <summary>
        /// reactToCharacter's "is the attacker the player": the host's player,
        /// or a joiner's proxy.
        /// </summary>
        public static Component PlayerOrProxyOn(Transform t)
        {
            if (t == null)
            {
                return null;
            }

            var p = t.GetComponent<Player>();
            if (p != null)
            {
                return p;
            }

            return ProxyOf(t);
        }

        /// <summary>
        /// reactToCharacter's attackPlayer(): a joiner who hit or bumped the
        /// enemy is the one it goes for.
        /// </summary>
        public static void AttackWhoReacted(Character c, Transform attacker)
        {
            var proxy = ProxyOf(attacker);
            if (proxy == null)
            {
                c.attackPlayer();
                return;
            }

            // An NPC turns on the joiner here, and the host runs it as an enemy
            // from then on (Combat.HostileNpcs).
            GoFor(c, proxy.transform);
            if (Threat.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[threat] {c.name} reacts to {proxy.OwnerName}: goes for "
                        + "them");
            }
        }

        /// <summary>
        /// Sends a character after a player, as Character.attackPlayer does for
        /// the host's (a defensive one stays defensive).
        /// </summary>
        public static void GoFor(Character c, Transform player)
        {
            bool defensive = c.aggressiveness == Aggressiveness.defensive;
            if (JoinerAiRules.AttacksOnSight(defensive))
            {
                c.aggressiveness = Aggressiveness.attackOnSight;
            }

            c.attackCharacter(player);
        }

        private static Location HostLocation()
        {
            var w = Player.Instance.whereAmI;
            if (w != null)
            {
                return w.bigLocation;
            }

            return null;
        }

        /// <summary>
        /// A hatching spider's location: the host's, unless a joiner is the
        /// nearest player; then where it is.
        /// </summary>
        public static Location HatchLocation(Character c)
        {
            var hostLoc = HostLocation();
            bool on = Threat.Enabled;
            bool atHost = false;
            if (on && c != null)
            {
                atHost = Threat.NearestPlayerPos(c)
                    == Player.Instance.transform.position;
            }

            if (JoinerAiRules.HatchesAtHost(on, c != null, atHost))
            {
                return hostLoc;
            }

            return null; // null: the game uses transform.GetLocation()
        }

        /// <summary>
        /// A spawned character's activity (getFromLocation) takes its target
        /// from "the player's" location: for one sent at a joiner's hideout
        /// that hideout, for others the location of the player it is after.
        /// </summary>
        public static Location SpawnedLocation(Character.Activity a)
        {
            var hostLoc = HostLocation();
            var activity = FactsOf(a, out var proxy, out var loc);
            bool asks = activity.On && activity.Exists
                && !(activity.Sent && activity.SentLocation);
            if (asks && proxy == null)
            {
                activity = activity with
                {
                    BestStandIn = BestStandIn(a, out proxy),
                };
            }

            Location at = null;
            if (asks && proxy != null)
            {
                at = NightSpawns.BigLocationAt(proxy.transform.position);
                activity = activity with { StandInLocation = at != null };
            }

            var pick = JoinerAiRules.SpawnedLocation(activity);
            if (pick == LocationPick.Sent)
            {
                return loc;
            }

            if (pick == LocationPick.StandIn)
            {
                return at;
            }

            return hostLoc;
        }

        /// <summary>
        /// An activity aimed at "the player" (playerIsTarget): the joiner it
        /// was sent at, or the most-threat player.
        /// </summary>
        public static GameObject ActivityPlayer(Character.Activity a)
        {
            var activity = FactsOf(a, out var proxy, out _);
            CharBase best = null;
            if (activity.On && activity.Exists
                && !(activity.Sent && activity.SentStandIn
                    && activity.SentAlive))
            {
                best = Threat.BestPlayerFor(a.thisCharacter);
                activity = activity with { BestPlayer = best != null };
            }

            var pick = JoinerAiRules.ActivityPlayer(activity);
            if (pick == PlayerPick.Sent)
            {
                return proxy.gameObject;
            }

            if (pick == PlayerPick.Best)
            {
                return best.gameObject;
            }

            return Player.Instance.gameObject;
        }

        /// <summary>
        /// What the activity's character was sent at (NightSpawns.SentAt);
        /// the "best" reads are left to the caller, which makes them only
        /// when the rule needs them.
        /// </summary>
        private static ActivityFacts FactsOf(
            Character.Activity a, out NetPlayerProxy proxy, out Location loc)
        {
            proxy = null;
            loc = null;
            Character c = null;
            if (a != null)
            {
                c = a.thisCharacter;
            }

            var activity = new ActivityFacts
            {
                On = Threat.Enabled,
                Exists = c != null,
            };

            if (!activity.On || !activity.Exists)
            {
                return activity;
            }

            bool sent = NightSpawns.SentAt(c, out proxy, out loc);
            return activity with
            {
                Sent = sent,
                SentLocation = loc != null,
                SentStandIn = proxy != null,
                SentAlive = proxy != null && proxy.alive,
            };
        }

        /// <summary>Whether "the player" by threat is a stand-in.</summary>
        private static bool BestStandIn(
            Character.Activity a, out NetPlayerProxy proxy)
        {
            proxy = Threat.BestPlayerFor(a.thisCharacter) as NetPlayerProxy;
            return proxy != null;
        }
    }
}
