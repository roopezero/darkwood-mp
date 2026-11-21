using System.Collections.Generic;
using System.Collections;
using DarkwoodMP.Combat;
using UnityEngine;

namespace DarkwoodMP.World.Culling
{
    /// <summary>
    /// The game shows (and runs) only the part of the world near the player:
    /// WorldGrid enters the grid's nodes near the player and leaves the rest.
    /// With other players in the host's world, the nodes near each of them are
    /// kept entered too, so the enemies near them are there and awake. While
    /// the host's own player is in a separate location or a dream,
    /// WorldGrid.currentGrid is that location's grid
    /// (OutsideLocations.transportToLocation leaves every node of the world's
    /// grid and calls setGrid), so the world's grid is also walked, around the
    /// other players only, and the enemies near them keep running. In the same
    /// way, the grid of each separate location the host runs for a joiner in it
    /// (NetPlayerProxy.InLocation, World.Locations.KeepRunning) is walked
    /// around the joiners there, whether or not the host's player is in it.
    ///
    /// One pass runs per grid at a time: a new refresh stops the grid's pass
    /// still running (one that entered one node a frame round a centre that has
    /// since jumped would otherwise leave, with the old centres, nodes the new
    /// one had just entered, and the locations there would flicker out and in
    /// again, running their exit and enter events).
    /// The nodes it entered stay entered, so the next pass goes on from there.
    /// The area kept round another player is what their own game keeps round
    /// them (ViewRects: their screen may be another shape than ours). While the
    /// host's own player is being taken to a place (OutsideLocations.loading,
    /// when the game's refreshPlayerPos skips its refresh), the others' areas
    /// are still entered round them (RefreshWhileLoading).
    /// </summary>
    internal sealed class SharedCulling
    {
        private bool _wasAway;
        /// <summary>
        /// Host: our own player is in a separate location or a dream (the
        /// world's grid isn't the current one).
        /// </summary>
        public bool HostAway => _wasAway;

        private readonly Dictionary<WorldGrid.Grid, Coroutine> _running =
            new Dictionary<WorldGrid.Grid, Coroutine>();
        private float _loadingRefreshAt;

        private struct Centre
        {
            public Vec2 Pos;
            public ViewSize Area;
        }

        public bool OnRefresh(WorldGrid wg, in GridRefresh call)
        {
            Vector3 pos = call.Pos;
            bool instant = call.Instant;
            bool force = call.Force;
            switch (CullingRules.Which(FactsOf(wg, instant)))
            {
                case RefreshKind.OwnPass:
                    // Alone (or a joiner): the game's own refresh, except that
                    // its passes don't overlap either. A jump (a respawn, a
                    // trip back from a place) otherwise has an older pass
                    // leave what the newer one entered.
                    Distances(out int dx, out int dy);
                    Run(wg, wg.currentGrid, new List<Centre>
                    {
                        CentreAt(pos, dx, dy)
                    }, false, force);

                    return false;
                case RefreshKind.Shared:
                    RefreshAll(
                        wg, wg.currentGrid, pos, instant,
                        force);
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        coop.FarEvents.Tick();
                    }

                    return false;
                default:
                    return true;
            }
        }

        private static RefreshFacts FactsOf(WorldGrid wg, bool instant)
        {
            var s = Net.Session.Instance;
            return new RefreshFacts
            {
                Grid = wg.currentGrid != null,
                StandIns = Proxies.Any,
                Instant = instant,
                Session = s != null && s.Active,
            };
        }

        private static Centre CentreAt(Vector3 pos, int dx, int dy)
        {
            return new Centre
            {
                Pos = new Vec2(pos.x, pos.z),
                Area = new ViewSize { Dx = dx, Dy = dy },
            };
        }

        /// <summary>
        /// Host, every frame: while our own player is taken to a place
        /// (OutsideLocations.loading: the loading screen, a few seconds), the
        /// game's refreshPlayerPos skips its refresh. The others' areas are
        /// still entered round them as they move: the world's grid round the
        /// players in the world, and the grid of each location run for a joiner
        /// round the joiners there. Nothing is left meanwhile (the game is
        /// switching our own grids over); the first refresh after the load
        /// leaves what's too far.
        /// </summary>
        public void RefreshWhileLoading()
        {
            var wg = Singleton<WorldGrid>.Instance;
            Tick tick = CullingRules.WhileLoading(LoadingFactsOf(wg));
            _loadingRefreshAt = tick.Next.Value;
            if (!tick.Due)
            {
                return;
            }

            var world = wg.getGrid("World");
            var grids = new Dictionary<WorldGrid.Grid, List<Centre>>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                var g = GridOf(wg, world, p);
                if (g == null)
                {
                    continue;
                }

                if (!grids.TryGetValue(g, out var list))
                {
                    grids[g] = list = new List<Centre>();
                }

                list.Add(Of(p));
            }

            foreach (var kv in grids)
            {
                if (_running.TryGetValue(kv.Key, out var old) && old != null)
                {
                    wg.StopCoroutine(old);
                }

                _running[kv.Key] =
                    wg.StartCoroutine(EnterSpread(kv.Key, kv.Value));
            }

            if (grids.Count > 0)
            {
                LoadingRefreshes++;
            }
        }

        /// <summary>
        /// Read every frame: with no other players nothing more is read.
        /// </summary>
        private LoadingFacts LoadingFactsOf(WorldGrid wg)
        {
            if (!Proxies.Any)
            {
                return new LoadingFacts
                {
                    StandIns = false,
                    Next = new Seconds(_loadingRefreshAt),
                };
            }

            var o = Singleton<OutsideLocations>.Instance;
            return new LoadingFacts
            {
                StandIns = Proxies.Any,
                Loading = o != null && o.loading,
                Refreshes = wg != null && !wg.disableRefresh,
                Player = Player.Instance != null,
                LoadingGame = Core.loadingGame,
                WorldMade = Core.worldGenFinished(),
                Now = new Seconds(Time.realtimeSinceStartup),
                Next = new Seconds(_loadingRefreshAt),
            };
        }

        private static IEnumerator EnterSpread(
            WorldGrid.Grid g, List<Centre> centres)
        {
            var nodes = g.nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].entered && Near(nodes[i].position, centres))
                {
                    nodes[i].enter();
                    yield return null;
                }
            }
        }

        /// <summary>
        /// For the tests: refreshes done while our player was being taken to a
        /// place.
        /// </summary>
        public int LoadingRefreshes { get; private set; }

        private void RefreshAll(
            WorldGrid wg, WorldGrid.Grid current, Vector3 pos, bool instant,
            bool force)
        {
            Distances(out int hx, out int hy);
            var centres = new List<Centre> { CentreAt(pos, hx, hy) };
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p != null)
                {
                    centres.Add(Of(p));
                }
            }

            var world = wg.getGrid("World");
            bool away = CullingRules.HostAway(world != null, world == current);
            // The other grids, each with the players in it: the world's (only
            // while we're away), and the locations run for joiners.
            var others = new Dictionary<WorldGrid.Grid, List<Centre>>();
            if (away)
            {
                others[world] = new List<Centre>();
            }

            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                var g = GridOf(wg, world, p);
                if (g == null || g == current)
                {
                    continue;
                }

                if (!others.TryGetValue(g, out var list))
                {
                    others[g] = list = new List<Centre>();
                }

                list.Add(Of(p));
            }

            if (away != _wasAway)
            {
                _wasAway = away;
                var log = Plugin.Log;
                string message = "The host is back in the world";
                if (away)
                {
                    string name = current.name;
                    int count = 0;
                    if (others.TryGetValue(world, out var w))
                    {
                        count = w.Count;
                    }

                    message =
                        $"The host is away ({name}): the world is kept running "
                            + $"around {count} other player(s)";
                }

                log.LogInfo(message);
            }

            Run(wg, current, centres, instant, force);
            foreach (var kv in others)
            {
                Run(wg, kv.Key, kv.Value, instant, force);
            }
        }

        /// <summary>
        /// Refresh a grid, stopping the pass still running on it first.
        /// </summary>
        private void Run(
            WorldGrid wg, WorldGrid.Grid g, List<Centre> centres, bool instant,
            bool force)
        {
            if (_running.TryGetValue(g, out var old))
            {
                if (old != null)
                {
                    wg.StopCoroutine(old);
                }

                _running.Remove(g);
            }

            if (instant)
            {
                Refresh(g, centres, force);
            }
            else
            {
                _running[g] =
                    wg.StartCoroutine(RefreshSpread(g, centres, force));
            }
        }

        internal static void Distances(out int dx, out int dy)
        {
            ViewSize area = CullingRules.HostArea(new ScreenFacts
            {
                Width = Screen.width,
                Height = Screen.height,
                WidthModifier = Core.ResolutionWidthModifier,
                HeightModifier = Core.ResolutionHeightModifier,
                Zoom = Singleton<Controller>.Instance.cameraZoom,
            });

            dx = area.Dx;
            dy = area.Dy;
        }

        /// <summary>
        /// The grid a player is in: their separate location's, the world's, or
        /// none (away in a place the host doesn't run).
        /// </summary>
        private static WorldGrid.Grid GridOf(
            WorldGrid wg, WorldGrid.Grid world, NetPlayerProxy p)
        {
            switch (CullingRules.GridOf(p.InLocation != null, p.Away))
            {
                case PlayerGrid.Location:
                    return wg.getGrid(
                        Core.getTrueLocationName(p.InLocation.name));
                case PlayerGrid.None:
                    return null;
                default:
                    return world;
            }
        }

        /// <summary>
        /// Another player's centre, with the rect their own game keeps running
        /// (ViewRects).
        /// </summary>
        private static Centre Of(NetPlayerProxy p)
        {
            ViewRects.For(p.Owner, out int dx, out int dy);
            return CentreAt(p.transform.position, dx, dy);
        }

        private static bool Near(Vector2 node, List<Centre> centres)
        {
            var at = new Vec2(node.x, node.y);
            foreach (var c in centres)
            {
                if (CullingRules.Near(at, c.Pos, c.Area))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Refresh(
            WorldGrid.Grid g, List<Centre> centres, bool force)
        {
            foreach (var n in g.nodes)
            {
                if (Near(n.position, centres))
                {
                    n.enter(force);
                }
            }

            foreach (var n in g.nodes)
            {
                if (!Near(n.position, centres))
                {
                    n.leave(force); // after the enters (see RefreshSpread)
                }
            }
        }

        // As the game's refreshNodes: enter one node a frame, so showing a
        // region doesn't stall a frame. The nodes to enter go first and the
        // ones to leave after: a location (a large cullable) spans several
        // nodes and is left when none of them is entered, so leaving its far
        // node before entering its near one would switch it off and on again
        // (its exit and enter events, its characters sent home).
        private static IEnumerator RefreshSpread(
            WorldGrid.Grid g, List<Centre> centres, bool force)
        {
            var nodes = g.nodes;
            var leave = new List<WorldGrid.Node>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (Near(nodes[i].position, centres))
                {
                    if (CullingRules.Enters(force, nodes[i].entered))
                    {
                        nodes[i].enter(force);
                        yield return null;
                    }
                }
                else
                {
                    leave.Add(nodes[i]);
                }
            }

            foreach (var n in leave)
            {
                n.leave(force);
            }
        }
    }
}
