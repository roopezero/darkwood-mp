using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for threat among several remote players.</summary>
    public static partial class MapScenarioMulti
    {
        // ---- 1. threat among several remote players -------------------------

        private static Character _enemy;   // host: the one mt-spawn made
        private static uint _enemyId;      // its net ID
        // host: who it was after, in order, and for each time it was after
        // someone, who else was nearer to it then
        private static readonly List<string> _targets = new List<string>();
        private static readonly Dictionary<string, HashSet<string>>
            _nearerWhile = new Dictionary<string, HashSet<string>>();

        private static string NameOf(Transform t)
        {
            if (t == null)
            {
                return "-";
            }

            if (P != null && t == P.transform)
            {
                return "host";
            }

            var px = t.GetComponent<NetPlayerProxy>();
            if (px != null)
            {
                return px.OwnerName;
            }

            return t.name;
        }

        /// <summary>
        /// The name a client game's player goes by on the host ("c2" is
        /// Client2, as tools/setup-client-game.sh names them).
        /// </summary>
        private static string PlayerName(string who)
        {
            if (who == "host")
            {
                return "host";
            }

            if (who.StartsWith("c"))
            {
                return "Client" + who.Substring(1);
            }

            return who;
        }

        private static IEnumerator WatchEnemy(Character c)
        {
            string last = null;
            while (c != null && c == _enemy && c.alive)
            {
                string now = NameOf(c.target);
                if (now != last)
                {
                    _targets.Add(now);
                    last = now;
                    Plugin.Log.LogInfo(
                        $"[map] {c.name} #{_enemyId} now after {now}");
                }

                if (c.target != null && now != "-")
                {
                    float mine = StateDump.Flat(
                        c.target.position - c.transform.position);
                    var players = NetPlayerProxy.All.Values.Where(
                        x => x != null
                            && x.alive)
                        .Select(x => (x.OwnerName, x.transform.position))
                        .ToList();
                    if (P != null)
                    {
                        players.Add(("host", P.transform.position));
                    }

                    foreach (var (name, pos) in players)
                    {
                        if (name != now
                            && StateDump.Flat(pos - c.transform.position)
                                    + 30f < mine)
                        {
                            if (!_nearerWhile.TryGetValue(now, out var set))
                            {
                                _nearerWhile[now] = set = new HashSet<string>();
                            }

                            set.Add(name);
                        }
                    }
                }

                yield return null;
            }
        }

        private static string ThreatText(Character c)
        {
            if (c == null)
            {
                return "-";
            }

            return string.Join(
                ", ",
                Threat.Of(c).Values.Where(kv => kv.Key != null).Select(
                    kv => $"{NameOf(kv.Key.transform)} {kv.Value:0.0}"));
        }

        /// <summary>"mt-spawn KIND DX DZ" (host): an enemy at the centre plus
        /// (DX, DZ), not set on anyone; from now on who it goes after is
        /// recorded.</summary>
        private static IEnumerator SpawnEnemy(string[] a, Result r)
        {
            if (Plugin.Instance.Host.Session.IsClient)
            {
                r.Fail("enemies are the host's");
                yield break;
            }

            var spot = MapScenarioEnemies.FreeSpot(
                Centre + new Vector3(F(a[1]), 0f, F(a[2])));
            // spawnCharacterAround takes a non-zero offset times the distance
            // as the position itself.
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                P.gameObject, spot, 1f, a[0], nocturnal: false,
                attackPlayer: false);
            if (c == null)
            {
                r.Fail($"couldn't spawn {a[0]}");
                yield break;
            }

            c.transform.position = spot;
            _enemy = c;
            _targets.Clear();
            _nearerWhile.Clear();
            yield return new WaitForSeconds(1.5f);
            Coop.Current.Enemies.NetIdOf(c, out _enemyId);
            Plugin.Instance.StartCoroutine(WatchEnemy(c));
            r.Info =
                $"{c.name} #{_enemyId} at {Fmt(c.transform.position)}, health "
                    + $"{c.health}";
        }

        /// <summary>
        /// The host's enemy nearest to our player (this game's copy of it),
        /// alive or dead.
        /// </summary>
        private static Character HostEnemy(bool alive, float within = 4000f)
        {
            NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

            if (ne == null)
            {
                return null;
            }

            return Nearest(
                Object.FindObjectsOfType<Character>().Where(
                    x => x.alive == alive && NetEnemies.Eligible(x)
                        && ne.NetIdOf(x, out _)
                        && StateDump.Flat(
                            x.transform.position - P.transform.position)
                            < within), P.transform.position);
        }

        /// <summary>"mt-hit N DAMAGE [--kill]": walk to the host's enemy, hit
        /// it N times, and walk back to where we stood. It should be alive
        /// after; with --kill, hit until it's dead (N at most), and it should
        /// be.</summary>
        private static IEnumerator HitEnemy(string[] a, Result r)
        {
            int n = int.Parse(a[0]);
            float dmg = F(a[1]);
            var from = P.transform.position;
            var c = HostEnemy(true);
            if (c == null)
            {
                r.Fail("no enemy of the host's near us");
                yield break;
            }

            float was = c.health;
            for (int i = 0; i < n && c != null && c.alive; i++)
            {
                yield return Walk(c.transform.position, 80f);
                AsPlayer(
                    () => c.getHit(
                        dmg, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSeconds(0.4f);
            }

            bool kill = a.Contains("--kill");
            if (kill)
            {
                // a client settles the corpse 2 s after it dies
                yield return new WaitForSeconds(3f);
            }
            else
            {
                yield return Walk(from, 40f);
            }

            uint id = 0;
            if (c != null)
            {
                Coop.Current.Enemies.NetIdOf(c, out id);
            }

            string enemyName = "-";
            if (c != null)
            {
                enemyName = c.name;
            }

            float health = 0f;
            if (c != null)
            {
                health = c.health;
            }

            r.Info =
                $"{enemyName} #{id}: hits of {dmg:0}, health {was:0} -> "
                    + $"{health:0}, alive {c != null && c.alive}";
            if (kill && c != null && c.alive)
            {
                r.Fail($"{r.Info}: still alive");
            }

            if (!kill && (c == null || !c.alive))
            {
                r.Fail($"{r.Info}: it died (the test wanted it alive)");
            }
        }

        /// <summary>"mt-target WHO [SECS] [--nearer OTHER]" (host): within SECS
        /// (default 15) the enemy goes after WHO's player. With --nearer OTHER:
        /// while it was after WHO, OTHER was nearer to it at some moment (it
        /// went by threat, not by who is closest).</summary>
        private static IEnumerator Target(string[] a, Result r)
        {
            var c = _enemy;
            if (c == null)
            {
                r.Fail("no enemy (mt-spawn first)");
                yield break;
            }

            string want = PlayerName(a[0]);
            float secs = 15f;
            if (a.Length > 1 && !a[1].StartsWith("--"))
            {
                secs = F(a[1]);
            }

            int ni = Array.IndexOf(a, "--nearer");
            string nearer = null;
            if (ni >= 0)
            {
                nearer = PlayerName(a[ni + 1]);
            }

            float until = Time.realtimeSinceStartup + secs;
            while (c != null && c.alive && NameOf(c.target) != want
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (nearer != null)
            {
                // Give it a moment after it turned to WHO, to be overtaken by
                // nobody and pass OTHER on the way.
                float more = Time.realtimeSinceStartup + 4f;
                while (c != null && c.alive
                    && Time.realtimeSinceStartup < more
                    && !(_nearerWhile.TryGetValue(want, out var s0)
                        && s0.Contains(nearer)))
                {
                    yield return null;
                }
            }

            Transform t = null;
            if (c != null)
            {
                t = c.target;
            }

            r.Info =
                $"after {NameOf(t)}; threat: {ThreatText(c)}; targets so far: "
                    + $"{string.Join(" > ", _targets)}";
            if (c == null || !c.alive)
            {
                r.Fail($"{r.Info}: it's dead");
                yield break;
            }

            if (NameOf(c.target) != want)
            {
                r.Fail($"{r.Info}: not after {want}");
            }
            else if (nearer != null
                && !(_nearerWhile.TryGetValue(want, out var s) && s.Contains(
                    nearer)))
            {
                r.Fail(
                    $"{r.Info}: {nearer} was never nearer to it while it was "
                        + $"after {want} (the test shows nothing)");
            }
        }

        /// <summary>
        /// "mt-never WHO" (host): the enemy never went after WHO's player.
        /// </summary>
        private static IEnumerator Never(string[] a, Result r)
        {
            string who = PlayerName(a[0]);
            r.Info = $"targets: {string.Join(" > ", _targets)}";
            if (_targets.Contains(who))
            {
                r.Fail($"{r.Info}: it went after {who}");
            }

            yield break;
        }

        /// <summary>
        /// "mt-dead" (any game): our copy of the host's enemy (the one nearest
        /// us) is dead and its corpse searchable.
        /// </summary>
        private static IEnumerator Dead(string[] a, Result r)
        {
            Character c = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                c = HostEnemy(false);
                if (c != null && c.GetComponent<Item>() != null
                    && c.GetComponentInChildren<Inventory>(true) != null)
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            var alive = HostEnemy(true, 2500f);
            if (c == null)
            {
                string livingName = "none";
                if (alive != null)
                {
                    livingName = alive.name;
                }

                r.Fail(
                    "no dead enemy of the host's near us (a living one: "
                        + $"{livingName})");
                yield break;
            }

            var inv = c.GetComponentInChildren<Inventory>(true);
            Coop.Current.Enemies.NetIdOf(c, out uint id);
            string name = c.name;
            string spot = Fmt(c.transform.position);
            bool searchable = c.GetComponent<Item>() != null;
            string loot = "-";
            if (inv != null)
            {
                loot = MapScenarioWorld.Contents(inv);
            }

            r.Info =
                $"{name} #{id} dead at {spot}, searchable {searchable}, loot "
                    + $"[{loot}]";
            if (c.GetComponent<Item>() == null || inv == null)
            {
                r.Fail($"{r.Info}: its corpse can't be searched here");
            }
        }

        /// <summary>"mt-corpse [--take]": walk to the host's dead enemy nearest
        /// us and open its body; with --take, take its first thing. Fails if
        /// the host refuses.</summary>
        private static IEnumerator Corpse(string[] a, Result r)
        {
            var c = HostEnemy(false);
            Inventory inv = null;
            if (c != null)
            {
                inv = c.GetComponentInChildren<Inventory>(true);
            }

            if (c == null || inv == null || c.GetComponent<Item>() == null)
            {
                string corpseName = "none";
                if (c != null)
                {
                    corpseName = c.name;
                }

                r.Fail($"no searchable corpse near us ({corpseName})");
                yield break;
            }

            yield return Walk(c.transform.position, 90f);
            int mark = MapScenarioIssues.LogMark();
            P.openedItemInventory = inv;
            // the host's contents come back (ContainerSlotsAll)
            yield return new WaitForSeconds(1.5f);
            string took = "looked";
            if (a.Contains("--take"))
            {
                var slot = inv.slots.FirstOrDefault(
                    s => !InvItemClass.isNull(s.invItem));
                if (slot == null)
                {
                    took = "nothing in it";
                }
                else
                {
                    var it = new InvItemClass(slot.invItem);
                    int before = MapScenarioWorld.CarriedOf(it.type);
                    AsPlayer(() =>
                    {
                        slot.removeItem();
                        P.Inventory.addItemTypeToPlayer(
                            it.type, it.amount, dropIfNoRoom: true);
                    });

                    yield return new WaitForSeconds(1f);
                    took =
                        $"took {it.type} x{it.amount} (we carry {before} -> "
                            + $"{MapScenarioWorld.CarriedOf(it.type)})";
                }
            }

            if (P.openedItemInventory == inv)
            {
                P.openedItemInventory = null;
            }

            yield return new WaitForSeconds(1.5f);
            var refused = MapScenarioIssues.LogLines().Skip(mark)
                .FirstOrDefault(
                    l => !l.Contains("[map]")
                        && (l.Contains("refused") || l.Contains(
                            "is in use by")));
            r.Info =
                $"{c.name} "
                    + $"{ObjRef.Of(inv.gameObject)}: {took}; "
                    + $"it holds [{MapScenarioWorld.Contents(inv)}]";
            if (refused != null)
            {
                r.Fail($"{r.Info}; {refused.Trim()}");
            }
        }
    }
}
