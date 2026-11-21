using System.Collections.Generic;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    internal sealed partial class WorldEvents
    {
        // ---- the day's redneck ambush ---------------------------------------

        private static readonly System.Reflection.MethodInfo RemoveMe =
            AccessTools.Method(typeof(RandomEvent), "removeMe");

        /// <summary>
        /// Host: the day's redneck ambush (RandomEvent spawnRedneck) can be
        /// any player's (AmbushRules). The game sends one redneck at the
        /// host's player when it's outdoors, away from any location, from day
        /// 2. With others playing, the ambush picks at random among every
        /// player that holds for (the host's player, and each living joiner's
        /// proxy that is outdoors and away from any location); for a joiner
        /// the redneck is spawned around their proxy and goes for them, as
        /// the game's does for the host. It's the host's enemy like any
        /// other, so it reaches the joiners as an EnemySpawn. Whether the
        /// game's own fire runs.
        /// </summary>
        public bool Ambush(RandomEvent e, AmbushGateFacts call)
        {
            bool redneck = e.type == RandomEvent.Type.spawnRedneck;
            if (!AmbushRules.Ours(
                Client, Plugin.Instance.Host.Threats.Enabled, redneck))
            {
                return true;
            }

            // The game's own gate (it returns before doing anything): left to
            // it.
            if (!AmbushRules.GameWants(GateOf(e, call)))
            {
                return true;
            }

            var ctl = Singleton<Controller>.Instance;
            int day = 0;
            if (ctl != null)
            {
                day = ctl.day;
            }

            if (!AmbushRules.DayAllows(ctl != null, day))
            {
                return true;
            }

            bool hostCan = AmbushRules.HostCan(HostOf(Player.Instance));
            var joiners = AmbushedJoiners();
            int choices = AmbushRules.Choices(joiners.Count, hostCan);
            if (choices == 0)
            {
                // only the host's player could be ambushed: the game's own
                return true;
            }

            int roll = UnityEngine.Random.Range(0, choices);
            int pick = AmbushRules.Target(joiners.Count, roll);
            if (pick < 0)
            {
                return true; // the host's player: the game's own
            }

            SendRedneck(e, joiners[pick]);
            return false;
        }

        /// <summary>
        /// The game's gate on this fire call: the call's arguments, and the
        /// event's state (requirementsMet() asked only when the game would).
        /// </summary>
        private static AmbushGateFacts GateOf(
            RandomEvent e, AmbushGateFacts call)
        {
            var gate = call with
            {
                Disabled = e.disabled,
                StartedToday = e.startedToday,
                MultipleTimesPerDay = e.multipleTimesPerDay,
            };

            if (AmbushRules.AsksRequirements(gate))
            {
                gate = gate with { RequirementsMet = e.requirementsMet() };
            }

            return gate;
        }

        private static AmbushHostFacts HostOf(Player host)
        {
            if (host == null)
            {
                return new AmbushHostFacts();
            }

            bool placed = host.whereAmI != null;
            bool atLocation = false;
            if (placed)
            {
                atLocation = host.whereAmI.bigLocation != null;
            }

            return new AmbushHostFacts
            {
                Exists = true,
                Alive = host.alive,
                Inside = host.isInside,
                Placed = placed,
                AtLocation = atLocation,
            };
        }

        /// <summary>The joiners' proxies that can be ambushed.</summary>
        private static List<NetPlayerProxy> AmbushedJoiners()
        {
            var joiners = new List<NetPlayerProxy>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                // Each is looked up only while the ones before it hold.
                Vector3 pos = p.transform.position;
                bool inside = false;
                bool atLocation = false;
                if (p.alive)
                {
                    inside = JoinerAi.InsideAt(pos);
                }

                if (p.alive && !inside)
                {
                    atLocation = NightSpawns.BigLocationAt(pos) != null;
                }

                if (AmbushRules.JoinerCan(p.alive, inside, atLocation))
                {
                    joiners.Add(p);
                }
            }

            return joiners;
        }

        /// <summary>
        /// The redneck spawned around a joiner's proxy, going for them, and
        /// the event counted as fired as the game's own would.
        /// </summary>
        private static void SendRedneck(RandomEvent e, NetPlayerProxy proxy)
        {
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                proxy.gameObject, Vector3.zero, AmbushRules.Radius,
                AmbushRules.Prefab, nocturnal: false, attackPlayer: false,
                relentlessPursuit: true);
            if (c != null)
            {
                if (c.aggressiveness != Aggressiveness.defensive)
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                c.attackCharacter(proxy.transform);
                c.attackType = AttackType.hitAndRun;
                c.wantToDespawn = true;
                c.maxStamina = AmbushRules.Stamina;
                float distance = Core.trueDistance(
                    c.transform.position, proxy.transform.position);
                Plugin.Log.LogInfo(
                    $"[events] the day's redneck ambush is {proxy.OwnerName}'s "
                        + $"({distance:0} from them)");
            }

            e.randomizeStartTime();
            if (c != null)
            {
                e.startedToday = true;
                if (e.removeOnFire)
                {
                    System.Reflection.MethodInfo removeMe = RemoveMe;
                    if (removeMe != null)
                    {
                        removeMe.Invoke(e, null);
                    }
                }
            }
        }
    }
}
