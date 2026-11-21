using System.Collections;
using DarkwoodMP.Combat.Nights;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The night worms the host sends at every player.
    /// </summary>
    internal sealed partial class NightSpawner
    {
        /// <summary>
        /// Replaces CharacterSpawner.waitToSpawnWorm: every five seconds of a
        /// hard night (not dreaming), a night worm 1500 to 2000 from each
        /// player without a shadow ward, sent at that player. Joiners don't
        /// spawn worms.
        /// </summary>
        public IEnumerator WormLoop(CharacterSpawner spawner)
        {
            while (true)
            {
                yield return new WaitForSeconds(NightRules.WormEvery);
                if (NetEnemies.ClientMode || spawner == null)
                {
                    continue;
                }

                try
                {
                    Worms(spawner);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Night worm spawn failed: {e.Message}");
                }
            }
        }

        private void Worms(CharacterSpawner spawner)
        {
            var due = NightRules.WormsFor(WormsOf(spawner));
            if (due == WormDue.None)
            {
                return;
            }

            if (due == WormDue.HostAndProxies)
            {
                SpawnWorm(spawner, null);
            }

            foreach (var proxy in NightSpawns.LivingProxies())
            {
                if (NightSpawns.WormWanted(proxy))
                {
                    SpawnWorm(spawner, proxy);
                }
            }
        }

        /// <summary>
        /// The night and the host's player at a tick of the worm loop (a
        /// client or a missing spawner never get here).
        /// </summary>
        private static WormFacts WormsOf(CharacterSpawner spawner)
        {
            var host = Player.Instance;
            if (host == null)
            {
                return new WormFacts { HasSpawner = spawner != null };
            }

            // read in the game's order, each only when it still counts
            bool hard = Singleton<Controller>.Instance.isHardNight;
            bool day = false;
            bool dreaming = false;
            bool ignores = false;
            bool ward = false;
            if (hard)
            {
                day = Core.isDay();
            }

            if (hard && !day)
            {
                dreaming = Singleton<Dreams>.Instance.dreaming;
                ignores = host.ignoreNightSickness;
            }

            if (hard && !day && !dreaming && !ignores)
            {
                ward = host.effects.hasEffectType(
                    CharacterEffectType.shadowWard);
            }

            return new WormFacts
            {
                HasSpawner = spawner != null,
                HasHost = true,
                HardNight = hard,
                Day = day,
                Dreaming = dreaming,
                IgnoresSickness = ignores,
                HostWard = ward,
            };
        }

        /// <summary>
        /// A night worm around a proxy (or the host's player, when null), going
        /// for them.
        /// </summary>
        public Character SpawnWorm(
            CharacterSpawner spawner, NetPlayerProxy proxy)
        {
            Vector3 around;
            if (proxy != null)
            {
                around = proxy.transform.position;
            }
            else
            {
                around = Player.Instance._transform.position;
            }

            Vector3 pos = Core.randomPosAround(
                around, NightRules.WormNear, NightRules.WormFar,
                canBeInside: true, mustBeInsideGraph: false);
            var go = Core.AddPrefab(
                "characters/fakechars/NightWorms_01", pos,
                Quaternion.Euler(90f, Random.Range(0, 360), 0f), null);
            Character c = null;
            if (go != null)
            {
                c = go.GetComponent<Character>();
            }

            if (c == null)
            {
                return null;
            }

            if (proxy == null)
            {
                c.attackPlayer();
            }
            else
            {
                bool defensive =
                    c.aggressiveness == Aggressiveness.defensive;
                if (NightRules.WormOnSight(defensive))
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                c.attackCharacter(proxy.transform);
            }

            spawner.nocturnalCharacters.Add(c.gameObject);
            if (proxy != null && Threat.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[night] night worm sent at {proxy.OwnerName}, "
                        + $"{Core.trueDistance(pos, around):0} from them");
            }

            return c;
        }
    }
}
