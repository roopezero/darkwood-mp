using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for fixes that had no test in game
    /// (docs/INTERACTIONS.md gaps 24, 28, 40, 43-45, 50, 51, 58). The order
    /// they run in is tools/tests/map_scenario_fixchecks.py. Each does a thing
    /// in one game and checks it in that game; a step in the other game checks
    /// what reached it.
    ///
    /// The enemies here are spawned by the host at fixed offsets from the
    /// centre, and each game finds them again as the shared enemy (a NetEnemies
    /// id) nearest to that spot.
    /// </summary>
    public static partial class MapScenarioFixChecks
    {
        private static readonly Dictionary<string, Vector3> Spots =
            new Dictionary<string, Vector3>
            {
                // the dog for the health bar, the looks and the pre-death stage
                ["fx"] = new Vector3(-600f, 0f, 1100f),
                // 2600 from the host, 1200 from the client at "client"
                ["near"] = new Vector3(2600f, 0f, 0f),
                // 2200 from the host, 1700 from the client at "client"
                ["far"] = new Vector3(1400f, 0f, -1700f),
                ["client"] = new Vector3(1400f, 0f, 0f),
                ["spirit"] = new Vector3(0f, 0f, -600f),
                // clear of the stations, for a dropped match or flare
                ["drop"] = new Vector3(400f, 0f, 700f),
            };

        private static Vector3 Spot(string which)
        {
            if (Spots.TryGetValue(which, out var off))
            {
                return Centre + off;
            }

            return Centre;
        }

        public static void Register()
        {
            // Walking somewhere by name.
            MapScenario.Add("fx-goto", (a, r) => GoTo(a, r), timeout: 30f);
            // 43-45: enemy copies (health bar, looks, a pre-death stage).
            MapScenario.Add("fx-spawn", FxSpawn);
            MapScenario.Add("fx-hit", FxHit, timeout: 25f);
            MapScenario.Add("fx-hurt", FxHurt);
            MapScenario.Add("fx-predeath", FxPreDeath);
            MapScenario.Add("fx-crawling", FxCrawling, timeout: 30f);
            MapScenario.Add("fx-dead", FxDead);
            MapScenario.Add("fx-corpse", FxCorpse, timeout: 20f);
            // 28: an InteractiveItem's switch.
            MapScenario.Add("ii-setup", IiSetup);
            MapScenario.Add("ii-switch", IiSwitch, timeout: 25f);
            MapScenario.Add("ii-check", IiCheck);
            // 40: a held throwable dropped.
            MapScenario.Add("drop-held", DropHeld, timeout: 30f);
            MapScenario.Add("expect-dropped", ExpectDropped);
            // 50: enemies near a joiner count as near a player.
            MapScenario.Add("near-joiner", NearJoiner, timeout: 20f);
            // 51: the forest spirit and the inside of its target.
            MapScenario.Add("fx-inside", FxInside, timeout: 30f);
            MapScenario.Add("spirit", Spirit, timeout: 25f);
            // 58: slow motion in co-op.
            MapScenario.Add("slowmo", Slowmo, timeout: 20f);
            // 24: the night trader killed.
            MapScenario.Add("kill-trader", KillTrader, timeout: 40f);
            MapScenario.Add("trader-dream", TraderDream, timeout: 20f);

            // How the dead look (45): a corpse's shadow faded and its particles
            // stopped, in every game.
            StateDump.AddSection("enemylooks", ctx =>
            {
                var ne = NetEnemies.Instance;
                foreach (var c in StateDump.All<Character>())
                {
                    if (!NetEnemies.Eligible(c)
                        || !ctx.Near(c.transform.position))
                    {
                        continue;
                    }

                    if (ne == null || !ne.NetIdOf(c, out _))
                    {
                        continue;
                    }

                    if (NetEnemies.InPlay(c)
                        && !c.gameObject.activeInHierarchy
                        && !NetEnemies.ClientMode)
                    {
                        // culled on the host: not shared (see "enemies")
                        continue;
                    }

                    var e = ctx.Add(c.gameObject);
                    // a copy is alive while the host's is in play
                    e["inPlay"] = NetEnemies.InPlay(c);
                    if (NetEnemies.InPlay(c))
                    {
                        if (c.maxHealth > 0f)
                        {
                            e["health"] = Mathf.RoundToInt(
                                Mathf.Clamp01(c.health / c.maxHealth) * 100f);
                        }
                        else
                        {
                            e["health"] = -1;
                        }
                    }
                    else
                    {
                        tk2dBaseSprite sp = null;
                        if (c.shadow != null)
                        {
                            sp = c.shadow.GetComponent<tk2dBaseSprite>();
                        }

                        if (sp == null)
                        {
                            e["shadow"] = "none";
                        }
                        else if (sp.color.a < 0.1f)
                        {
                            e["shadow"] = "faded";
                        }
                        else
                        {
                            e["shadow"] = "shown";
                        }

                        e["particles"] =
                            c.GetComponentsInChildren<ParticleSystem>().Count(
                                ps => ps != null && ps.isEmitting);
                    }
                }
            });
        }

        // ---- helpers --------------------------------------------------------

        private static Player P => Player.Instance;

        private static IEnumerator GoTo(string[] a, Result r)
        {
            string which = "";
            if (a.Length > 0)
            {
                which = a[0];
            }

            var to = Spot(which);
            yield return Walk(to, 40f);
            r.Info = $"at {Fmt(P.transform.position)}";
        }

        /// <summary>
        /// The shared enemy (one with a net id) nearest to a spot.
        /// </summary>
        private static Character Shared(
            Vector3 at, Func<Character, bool> ok = null, float within = 900f)
        {
            var ne = NetEnemies.Instance;
            if (ne == null)
            {
                return null;
            }

            return Object.FindObjectsOfType<Character>().Where(
                c => c != null && NetEnemies.Eligible(c)
                    && ne.NetIdOf(c, out _) && (ok == null || ok(c))
                    && StateDump.Flat(c.transform.position - at) < within)
                .OrderBy(c => StateDump.Flat(c.transform.position - at))
                .FirstOrDefault();
        }

        /// <summary>The enemy's net id, or 0 if it has none.</summary>
        internal static uint IdOf(Character c)
        {
            uint id = 0;
            NetEnemies instance = NetEnemies.Instance;
            if (instance != null)
            {
                instance.NetIdOf(c, out id);
            }

            return id;
        }

        /// <summary>
        /// A character of this type (not nocturnal, not sent at the player) on
        /// the free spot nearest `at`.
        /// </summary>
        internal static Character SpawnAt(Vector3 at, string type = "Dog")
        {
            var spot = MapScenarioEnemies.FreeSpot(at);
            // spawnCharacterAround takes a non-zero offset times the distance
            // as the position itself.
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                P.gameObject, spot, 1f, type, nocturnal: false,
                attackPlayer: false);
            if (c != null)
            {
                c.transform.position = spot;
            }

            return c;
        }

        /// <summary>
        /// Where the other player is in this game: the host's proxy for them,
        /// or their puppet.
        /// </summary>
        private static Transform Other()
        {
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p != null)
                {
                    return p.transform;
                }
            }

            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Owner != Sync.PlayerSync.DummyId && rp.Root != null)
                    {
                        return rp.Root.transform;
                    }
                }
            }

            return null;
        }

        private static float BarFraction(EnemyHealthBar bar)
        {
            if (bar != null && bar.full != null)
            {
                return bar.full.clipTopRight.x;
            }

            return -1f;
        }
    }
}
