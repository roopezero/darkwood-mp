using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DarkwoodMP.Combat.Nights;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: the night attackers and night worms it sends at every player
    /// (NightSpawns says how), and the attackers out at each hideout. One for
    /// the game (ModHost.Nights), as the test dummy brings a stand-in into a
    /// game played alone; the budgets start again each dawn.
    /// </summary>
    internal sealed partial class NightSpawner
    {
        private sealed class Sent
        {
            public NetPlayerProxy Proxy;
            public Location Loc;
        }

        // which hideout and kind each attacker counts against
        private sealed class Spent
        {
            public int Place;
            public string Kind;
        }

        private readonly Collider[] _near = new Collider[64];
        private readonly ConditionalWeakTable<Character, Sent> _sent =
            new ConditionalWeakTable<Character, Sent>();
        private readonly HideoutBudgets _budgets = new HideoutBudgets();
        private readonly ConditionalWeakTable<Character, Spent> _spent =
            new ConditionalWeakTable<Character, Spent>();

        /// <summary>
        /// Every attacker sent while playing together since the day began: its
        /// hideout's name, who it was aimed at (for the map test).
        /// </summary>
        public readonly List<(string Hideout, string At, Character C)>
            SentLog = new List<(string, string, Character)>();

        /// <summary>
        /// A night attacker this sent at a joiner's hideout: that joiner and
        /// the hideout (JoinerAi uses them for its activities).
        /// </summary>
        public bool SentAt(
            Character c, out NetPlayerProxy proxy, out Location loc)
        {
            proxy = null;
            loc = null;
            if (c == null || !_sent.TryGetValue(c, out var s))
            {
                return false;
            }

            proxy = s.Proxy;
            loc = s.Loc;
            return true;
        }

        /// <summary>
        /// The big location at a position, found the way WhereAmI.checkWhereAmI
        /// finds the player's.
        /// </summary>
        public Location BigLocationAt(Vector3 pos)
        {
            int n = Physics.OverlapSphereNonAlloc(
                pos, NightRules.PlaceRadius, _near, NightRules.PlaceLayers);
            var locs = new List<Location>();
            var distances = new List<float>();
            for (int i = 0; i < n; i++)
            {
                Location loc = null;
                if (_near[i] != null)
                {
                    loc = _near[i].GetComponent<Location>();
                }

                if (loc == null)
                {
                    continue;
                }

                locs.Add(loc);
                distances.Add(Core.trueDistance(pos, loc.transform.position));
            }

            int at = NightRules.NearestPlace(distances);
            if (at < 0)
            {
                return null;
            }

            var closest = locs[at];
            if (!closest.isSubLocation)
            {
                return closest;
            }

            var t = closest.transform;
            if (t.parent != null && t.parent.parent != null)
            {
                return t.parent.parent.GetComponent<Location>();
            }

            return null;
        }

        /// <summary>
        /// The attackers of a kind out at a hideout now (counted against its
        /// budget).
        /// </summary>
        public int OutAt(Location loc, string kind)
        {
            if (loc == null)
            {
                return 0;
            }

            return _budgets.Out(loc.GetInstanceID(), kind);
        }

        /// <summary>
        /// CharacterSpawner.spawnNightChar is due: true when the game's own
        /// runs (alone). Joiners send nobody; with other players the host
        /// sends one attacker for each occupied hideout.
        /// </summary>
        public bool NightCharDue(CharacterSpawner spawner)
        {
            var way = NightRules.WhoSpawns(NetEnemies.ClientMode, Proxies.Any);
            if (way != NightWay.Ours)
            {
                return way == NightWay.Game;
            }

            try
            {
                NightChars(spawner);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Night attacker spawn failed: {e.Message}");
            }

            return false;
        }

        /// <summary>
        /// A night attacker left CharacterSpawner's list (dead, or
        /// despawned): one fewer out at its hideout, at night.
        /// </summary>
        public void Removed(Character c)
        {
            if (c != null && NightRules.CountsGone(Core.isDay()))
            {
                Gone(c);
            }
        }

        /// <summary>
        /// Host, with other players: replaces CharacterSpawner.spawnNightChar.
        /// One attacker for each occupied hideout, aimed at the host's player
        /// if they are in it, else at the first joiner there.
        /// </summary>
        private void NightChars(CharacterSpawner spawner)
        {
            var proxies = new List<NetPlayerProxy>();
            var locs = new List<Location>();
            var players = new List<NightPlayer>();
            foreach (var p in new List<NetPlayerProxy>(
                NetPlayerProxy.All.Values))
            {
                if (p == null)
                {
                    continue;
                }

                proxies.Add(p);
                players.Add(PlayerOf(p, out var loc));
                locs.Add(loc);
            }

            var night = FactsOf(spawner, out var hostLoc);
            foreach (var h in NightRules.Hideouts(night, players))
            {
                if (h.Host)
                {
                    SpawnNightChar(spawner, null, hostLoc);
                }
                else
                {
                    SpawnNightChar(spawner, proxies[h.Proxy], locs[h.Proxy]);
                }
            }
        }

        /// <summary>
        /// The spawner and the host's player as NightRules.Hideouts reads them,
        /// and the host's big location.
        /// </summary>
        private static NightFacts FactsOf(
            CharacterSpawner spawner, out Location hostLoc)
        {
            hostLoc = null;
            if (spawner == null || !spawner.spawnNocturnalCharacters)
            {
                return new NightFacts
                {
                    HasSpawner = spawner != null,
                };
            }

            if (Singleton<Dreams>.Instance.dreaming)
            {
                return new NightFacts
                {
                    HasSpawner = true,
                    SpawnsNocturnal = true,
                    Dreaming = true,
                };
            }

            var host = Player.Instance;
            // The host's player as the game checks it (the game sends nobody
            // while the host is in an outside location).
            if (host != null && host.whereAmI != null)
            {
                hostLoc = host.whereAmI.bigLocation;
            }

            bool atPlace = hostLoc != null;
            int place = 0;
            bool hideout = false;
            if (atPlace)
            {
                place = hostLoc.GetInstanceID();
                hideout = hostLoc.playerBase;
            }

            var outside = Singleton<OutsideLocations>.Instance;
            return new NightFacts
            {
                HasSpawner = true,
                SpawnsNocturnal = true,
                HostOutside = outside.playerInOutsideLocation,
                HostAtPlace = atPlace,
                HostPlace = place,
                HostBase = hideout,
            };
        }

        /// <summary>
        /// A stand-in as NightRules.Hideouts reads it, and its big location
        /// (looked for only when it's alive and in the world).
        /// </summary>
        private NightPlayer PlayerOf(NetPlayerProxy p, out Location loc)
        {
            loc = null;
            bool separate = p.InLocation != null;
            if (p.alive && !separate)
            {
                loc = BigLocationAt(p.transform.position);
            }

            bool atPlace = loc != null;
            int place = 0;
            bool hideout = false;
            if (atPlace)
            {
                place = loc.GetInstanceID();
                hideout = loc.playerBase;
            }

            return new NightPlayer
            {
                Alive = p.alive,
                Separate = separate,
                AtPlace = atPlace,
                Place = place,
                Base = hideout,
            };
        }

        /// <summary>The body of CharacterSpawner.spawnNightChar, aimed at a
        /// proxy (or the host's player, when null) at the hideout loc, counted
        /// against that hideout's budget.</summary>
        public Character SpawnNightChar(
            CharacterSpawner spawner, NetPlayerProxy proxy, Location loc)
        {
            var scenarios = Singleton<NightScenarios>.Instance;
            if (scenarios.currentScenario == null)
            {
                scenarios.setCurrentScenario();
            }

            if (scenarios.currentScenario == null)
            {
                return null;
            }

            var all = scenarios.currentScenario.characters;
            var entries = new List<NightEntry>();
            foreach (var toSpawn in all)
            {
                entries.Add(EntryOf(toSpawn, loc));
            }

            int pick = NightRules.PickAttacker(entries);
            if (pick < 0)
            {
                return null;
            }

            return Spawn(spawner, proxy, loc, all[pick]);
        }

        private NightEntry EntryOf(
            NightScenario.CharacterToSpawn toSpawn, Location loc)
        {
            if (toSpawn == null)
            {
                return new NightEntry();
            }

            return new NightEntry
            {
                Name = toSpawn.characterName,
                Amount = toSpawn.amount,
                Out = OutAt(loc, toSpawn.characterName),
            };
        }

        private Character Spawn(
            CharacterSpawner spawner, NetPlayerProxy proxy, Location loc,
            NightScenario.CharacterToSpawn toSpawn)
        {
            GameObject at;
            if (proxy != null)
            {
                at = proxy.gameObject;
            }
            else
            {
                at = Player.Instance.gameObject;
            }

            var c = spawner.spawnCharacterAround(
                at, Vector3.zero, NightRules.AttackerAround,
                toSpawn.characterName, nocturnal: true);
            if (c == null)
            {
                return null;
            }

            if (proxy != null)
            {
                _sent.Remove(c);
                _sent.Add(c, new Sent { Proxy = proxy, Loc = loc });
                // spawnCharacterAround gave it the host's location's
                // waypoints (or none); this hideout's are the right ones.
                if (loc != null && loc.waypoints != null
                    && loc.waypoints.Count > 0)
                {
                    c.setWaypoints(loc.waypoints);
                }
            }

            if (NightRules.LaterChapter(ChapterOf())
                && NightRules.GetsShadowArmor(Random.Range(0f, 1f)))
            {
                c.gameObject.AddComponent<ShadowArmor>();
            }

            toSpawn.spawned++;
            if (loc != null)
            {
                int place = loc.GetInstanceID();
                _budgets.Spend(place, toSpawn.characterName);
                _spent.Remove(c);
                _spent.Add(c, new Spent
                {
                    Place = place,
                    Kind = toSpawn.characterName
                });
            }

            LogSent(c, proxy, loc, toSpawn);
            return c;
        }

        // the map test's log and ours
        private void LogSent(
            Character c, NetPlayerProxy proxy, Location loc,
            NightScenario.CharacterToSpawn toSpawn)
        {
            string who = "the host";
            GameObject at;
            if (proxy != null)
            {
                who = proxy.OwnerName;
                at = proxy.gameObject;
            }
            else
            {
                at = Player.Instance.gameObject;
            }

            string where = "-";
            if (loc != null)
            {
                where = loc.name;
            }

            SentLog.Add((where, who, c));
            float distance = Core.trueDistance(
                c.transform.position, at.transform.position);
            Plugin.Log.LogInfo(
                $"[night] {c.name} sent at {who}'s hideout {where} "
                    + $"({OutAt(loc, toSpawn.characterName)} of "
                    + $"{toSpawn.amount} {toSpawn.characterName} out "
                    + "there), " +
                               $"{distance:0} from them");
        }

        /// <summary>
        /// A night attacker is gone (CharacterSpawner.removeFromNightChars):
        /// one fewer out at its hideout.
        /// </summary>
        private void Gone(Character c)
        {
            if (c == null || !_spent.TryGetValue(c, out var s))
            {
                return;
            }

            _spent.Remove(c);
            _budgets.Gone(s.Place, s.Kind);
        }

        /// <summary>
        /// A new day (NightScenarios.resetScenarios): every hideout's budget
        /// starts again.
        /// </summary>
        public void NewDay()
        {
            _budgets.Clear();
            SentLog.Clear();
        }

        private static ChapterFacts ChapterOf()
        {
            if (Core.randomGeneration)
            {
                return new ChapterFacts
                {
                    RandomGeneration = true,
                    GeneratedChapter =
                        Singleton<WorldGenerator>.Instance.chapterID,
                };
            }

            var scene = GameObject.Find(Helpers.GetSceneName());
            Location l = null;
            if (scene != null)
            {
                l = scene.GetComponent<Location>();
            }

            if (l == null)
            {
                return new ChapterFacts();
            }

            return new ChapterFacts
            {
                HasScene = true,
                SceneChapter = l.chapterId,
            };
        }
    }
}
