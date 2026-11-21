using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DarkwoodMP.Combat.Targeting;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Threat-based targeting, on the host, while there are several players
    /// (the core's ThreatRules decide; see Threat for what counts). One lives
    /// as long as the game, in ModHost.Threats: the test dummy brings it into
    /// a game played alone too. Each enemy's ThreatTable is kept with the
    /// enemy, so it goes when the enemy does.
    /// </summary>
    internal sealed class Threats
    {
        private readonly ConditionalWeakTable<
            Character,
            ThreatTable<CharBase>> _tables =
            new ConditionalWeakTable<Character, ThreatTable<CharBase>>();

        private readonly Proxies _proxies;

        public Threats(Proxies proxies)
        {
            _proxies = proxies;
        }

        /// <summary>
        /// Who's making the noise being broadcast right now (set around
        /// Character.alertInArea), or null.
        /// </summary>
        public CharBase NoiseSource { get; set; }

        /// <summary>
        /// Log every target change with its scores ("--dwmp-debug-threat").
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// Only while the host has other players; alone, the game's own AI is
        /// left exactly as it was.
        /// </summary>
        public bool Enabled => ThreatRules.IsOn(_proxies.All.Count);

        public ThreatTable<CharBase> Of(Character c)
        {
            return _tables.GetValue(c, New);
        }

        public void OnDamaged(Character victim, CharBase attacker, float damage)
        {
            if (!ThreatRules.CountsHit(Enabled, Proxies.IsPlayer(attacker)))
            {
                return;
            }

            Of(victim).Add(attacker, damage, Now);
        }

        public void OnHeard(Character listener, Vector3 position, float volume)
        {
            CharBase source = NoiseSource;
            if (!Enabled || source == null)
            {
                return; // only noise a player made is theirs
            }

            float gain = ThreatRules.NoiseGain(volume);
            Of(listener).Add(source, gain, Now);
            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[threat] {listener.name} heard {Name(source)} "
                        + $"(+{gain:0.0}) at {position}");
            }
        }

        /// <summary>
        /// Before the game's sight check. The game targets whichever visible
        /// character comes last in its view-range list, and acts on it within
        /// the same check (stops to listen, chases, attacks), so the choice has
        /// to be made before it runs: the players in the list are put at its
        /// end, least threat first, so the one with the most threat is the one
        /// the game picks, if it can see them (ThreatRules.Order).
        /// </summary>
        public void BeforeSightCheck(Character c)
        {
            SightStep step = ThreatRules.BeforeSight(FactsOf(c));
            if (step == SightStep.FriendlyNpc)
            {
                LeaveToItsPlayer(c);
            }
            else if (step == SightStep.Reorder)
            {
                Reorder(c);
            }
        }

        /// <summary>
        /// After the game's sight check: every player it saw gains a little
        /// threat for being seen.
        /// </summary>
        public void AfterSightCheck(Character c)
        {
            if (!ThreatRules.CountsSeen(FactsOf(c)))
            {
                return;
            }

            var table = Of(c);
            Seconds now = Now;
            float gain = ThreatRules.SeenGain(table.TakeSeenSpan(now));
            foreach (var seen in c.charactersInSight)
            {
                bool player = Proxies.IsPlayer(seen);
                if (ThreatRules.GainsSeen(
                        player,
                        player && seen.alive,
                        player && seen.invisible))
                {
                    table.Add(seen, gain, now);
                }
            }
        }

        /// <summary>
        /// The player an enemy goes for when the game sends it after
        /// "the player" (Character.attackPlayer, enemies that pursue the player
        /// constantly): the one it has the most threat for, closeness counting,
        /// seen or not (ThreatRules.Best).
        /// </summary>
        public CharBase BestPlayerFor(Character c)
        {
            var host = Player.Instance;
            // A friendly NPC is each player's own; one the host runs as an
            // enemy goes by threat, as enemies do.
            if (!ThreatRules.ChoosesByThreat(FactsOf(c)))
            {
                return host;
            }

            var table = Of(c);
            table.Decay(Now);
            float range = ThreatRules.CloseRange(
                c.farViewDistance,
                c.aniSightRangeModifier);
            var who = new List<CharBase>();
            var candidates = new List<PlayerCandidate>();
            if (host != null)
            {
                who.Add(host);
                candidates.Add(CandidateOf(table, c, host));
            }

            foreach (var p in _proxies.All.Values)
            {
                if (p != null)
                {
                    who.Add(p);
                    candidates.Add(CandidateOf(table, c, p));
                }
            }

            int best = ThreatRules.Best(candidates, range);
            if (best < 0)
            {
                return host;
            }

            return who[best];
        }

        /// <summary>
        /// Where "the player" is for the game's distance checks on an enemy:
        /// the nearest player, the host's own (alive or not) or a living
        /// remote one (ThreatRules.Nearest).
        /// </summary>
        public Vector3 NearestPlayerPos(Character c)
        {
            var spots = new List<StandInSpot>();
            NearestFacts near = NearestOf(c, spots);
            Vec3 at = ThreatRules.Nearest(near, spots);
            return new Vector3(at.X, at.Y, at.Z);
        }

        /// <summary>
        /// The position an enemy runs away from when the game says
        /// "from the player": the player it is after, if its target is a
        /// player, or else the nearest player.
        /// </summary>
        public Vector3 FleeFrom(Character c)
        {
            if (ThreatRules.FleesFromTarget(FactsOf(c)))
            {
                return c.target.position;
            }

            return NearestPlayerPos(c);
        }

        private static Seconds Now => new Seconds(Time.time);

        private static ThreatTable<CharBase> New(Character c)
        {
            return new ThreatTable<CharBase>(Now, Gone);
        }

        private static bool Gone(CharBase who)
        {
            return who == null;
        }

        private EnemyFacts FactsOf(Character c)
        {
            if (!Enabled)
            {
                // Every rule refuses while off: alone, nothing is read.
                return new EnemyFacts { On = false };
            }

            bool exists = c != null;
            CharBase target = null;
            bool hasTarget = exists && c.target != null;
            if (hasTarget)
            {
                target = c.target.GetComponent<CharBase>();
            }

            return new EnemyFacts
            {
                On = true,
                Exists = exists,
                Alive = exists && c.alive,
                Npc = exists && c.npc != null,
                Eligible = exists && NetEnemies.Eligible(c),
                Seen = SeenCount(c),
                HasTarget = hasTarget,
                TargetIsPlayer = Proxies.IsPlayer(target),
            };
        }

        private static int SeenCount(Character c)
        {
            if (c == null || c.charactersInViewRange == null)
            {
                return 0;
            }

            return c.charactersInViewRange.Count;
        }

        /// A friendly NPC is each player's own: it doesn't see the joiners'
        /// stand-ins. A follower a joiner recruited follows them, not the
        /// host's player (World.NpcFollowers).
        private static void LeaveToItsPlayer(Character c)
        {
            CoopSession coop = Coop.Current;
            if (coop != null && coop.Followers.BeforeSightCheck(c))
            {
                return;
            }

            c.charactersInViewRange.RemoveAll(IsStandIn);
        }

        private static bool IsStandIn(Collider col)
        {
            return col != null && col.GetComponent<NetPlayerProxy>() != null;
        }

        private void Reorder(Character c)
        {
            var list = c.charactersInViewRange;
            var table = Of(c);
            table.Decay(Now);
            float sight = ThreatRules.SightRange(
                c.farViewDistance,
                c.aniSightRangeModifier);
            var players = new List<SightCandidate>();
            var others = new List<Collider>();
            for (int i = 0; i < list.Count; i++)
            {
                var col = list[i];
                CharBase cb = null;
                if (col != null)
                {
                    cb = col.GetComponent<CharBase>();
                }

                bool player = Proxies.IsPlayer(cb);
                if (ThreatRules.InSightOrder(player, player && cb.alive))
                {
                    players.Add(SightOf(table, c, cb, i));
                }
                else
                {
                    others.Add(col);
                }
            }

            if (!ThreatRules.Order(players, sight))
            {
                return;
            }

            if (Debug)
            {
                LogChoice(c, list, players);
            }

            var ordered = new List<Collider>(others);
            foreach (var p in players)
            {
                ordered.Add(list[p.Slot]);
            }

            list.Clear();
            list.AddRange(ordered);
        }

        private static SightCandidate SightOf(
            ThreatTable<CharBase> table,
            Character c,
            CharBase who,
            int slot)
        {
            return new SightCandidate
            {
                Slot = slot,
                Stored = table.Get(who),
                Distance = Vector3.Distance(
                    who.transform.position,
                    c.transform.position),
                Current = c.target == who.transform,
            };
        }

        private static PlayerCandidate CandidateOf(
            ThreatTable<CharBase> table,
            Character c,
            CharBase who)
        {
            bool dying = false;
            if (who is Player host)
            {
                dying = host.dying;
            }

            return new PlayerCandidate
            {
                Host = who is Player,
                Alive = who.alive,
                Dying = dying,
                Invisible = who.invisible,
                Stored = table.Get(who),
                Distance = Vector3.Distance(
                    who.transform.position,
                    c.transform.position),
            };
        }

        private NearestFacts NearestOf(Character c, List<StandInSpot> spots)
        {
            var host = Player.Instance;
            bool exists = c != null;
            var facts = new NearestFacts
            {
                On = Enabled,
                HasHost = host != null,
                Exists = exists,
            };

            if (exists)
            {
                facts = facts with { EnemyAt = VecOf(c.transform.position) };
            }

            if (host == null || !exists)
            {
                if (host != null)
                {
                    facts = facts with
                    {
                        HostAt = VecOf(host.transform.position),
                    };
                }

                return facts;
            }

            Vector3 me = c.transform.position;
            Vector3 hostAt = host.transform.position;
            facts = facts with
            {
                HostAt = VecOf(hostAt),
                HostDistance = Core.trueDistance(hostAt, me),
            };

            foreach (var p in _proxies.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                Vector3 at = p.transform.position;
                spots.Add(new StandInSpot
                {
                    Alive = p.alive,
                    At = VecOf(at),
                    Distance = Core.trueDistance(at, me),
                });
            }

            return facts;
        }

        private static Vec3 VecOf(Vector3 v)
        {
            return new Vec3(v.x, v.y, v.z);
        }

        private void LogChoice(
            Character c,
            List<Collider> list,
            List<SightCandidate> players)
        {
            var chosen = players[players.Count - 1];
            CharBase who = list[chosen.Slot].GetComponent<CharBase>();
            if (c.target != null && c.target == who.transform)
            {
                return;
            }

            var rest = new List<string>();
            for (int i = 0; i < players.Count - 1; i++)
            {
                var p = players[i];
                CharBase other = list[p.Slot].GetComponent<CharBase>();
                rest.Add($"{Name(other)} ({p.Score:0.0})");
            }

            Plugin.Log.LogInfo(
                $"[threat] {c.name} prefers {Name(who)} "
                    + $"({chosen.Score:0.0}) over "
                    + string.Join(", ", rest));
        }

        private static string Name(CharBase c)
        {
            if (c is NetPlayerProxy p)
            {
                return p.OwnerName;
            }

            if (c is Player)
            {
                return "host player";
            }

            return c.name;
        }
    }
}
