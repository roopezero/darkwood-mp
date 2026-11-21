using System.Collections.Generic;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
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
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.refreshPosition))]
    public static class SharedCullingPatch
    {
        private static bool _wasAway;
        /// <summary>
        /// Host: our own player is in a separate location or a dream (the
        /// world's grid isn't the current one).
        /// </summary>
        public static bool HostAway => _wasAway;

        private static readonly Dictionary<WorldGrid.Grid, Coroutine> _running =
            new Dictionary<WorldGrid.Grid, Coroutine>();
        private static float _loadingRefreshAt;

        private struct Centre
        {
            public Vector3 Pos;
            public int Dx, Dy;
        }

        private static bool Prefix(
            WorldGrid __instance, Vector3 pos, bool instant, bool force)
        {
            if (__instance.currentGrid == null)
            {
                return true;
            }

            if (!Proxies.Any)
            {
                // Alone (or a joiner): the game's own refresh, except that its
                // passes don't overlap either. A jump (a respawn, a trip back
                // from a place) otherwise has an older pass leave what the
                // newer one entered.
                var s = Net.Session.Instance;
                if (instant || s == null || !s.Active)
                {
                    return true;
                }

                Distances(out int dx, out int dy);
                Run(__instance, __instance.currentGrid, new List<Centre>
                {
                    new Centre { Pos = pos, Dx = dx, Dy = dy }
                }, false, force);

                return false;
            }

            RefreshAll(__instance, __instance.currentGrid, pos, instant, force);
            World.FarEvents.Tick();
            return false;
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
        public static void RefreshWhileLoading()
        {
            if (!Proxies.Any)
            {
                return;
            }

            var o = Singleton<OutsideLocations>.Instance;
            var wg = Singleton<WorldGrid>.Instance;
            if (o == null || !o.loading || wg == null || wg.disableRefresh
                || Player.Instance == null || Core.loadingGame
                || !Core.worldGenFinished())
            {
                return;
            }

            if (Time.realtimeSinceStartup < _loadingRefreshAt)
            {
                return;
            }

            _loadingRefreshAt = Time.realtimeSinceStartup + 0.5f;
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
        public static int LoadingRefreshes;

        private static void RefreshAll(
            WorldGrid wg, WorldGrid.Grid current, Vector3 pos, bool instant,
            bool force)
        {
            Distances(out int hx, out int hy);
            var centres =
                new List<Centre> { new Centre { Pos = pos, Dx = hx, Dy = hy } };
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p != null)
                {
                    centres.Add(Of(p));
                }
            }

            var world = wg.getGrid("World");
            bool away = world != null && world != current;
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
        private static void Run(
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
            float m = Mathf.Max(
                Core.ResolutionWidthModifier, Core.ResolutionHeightModifier);
            float zoom = Singleton<Controller>.Instance.cameraZoom;
            dx = (int)((Screen.width + 500f * m) / zoom);
            dy = (int)((Screen.height + 500f * m) / zoom);
        }

        /// <summary>
        /// The grid a player is in: their separate location's, the world's, or
        /// none (away in a place the host doesn't run).
        /// </summary>
        private static WorldGrid.Grid GridOf(
            WorldGrid wg, WorldGrid.Grid world, NetPlayerProxy p)
        {
            if (p.InLocation != null)
            {
                return wg.getGrid(Core.getTrueLocationName(p.InLocation.name));
            }

            if (p.Away)
            {
                return null;
            }

            return world;
        }

        /// <summary>
        /// Another player's centre, with the rect their own game keeps running
        /// (ViewRects).
        /// </summary>
        private static Centre Of(NetPlayerProxy p)
        {
            ViewRects.For(p.Owner, out int dx, out int dy);
            return new Centre { Pos = p.transform.position, Dx = dx, Dy = dy };
        }

        private static bool Near(Vector2 node, List<Centre> centres)
        {
            foreach (var c in centres)
            {
                if (Mathf.Abs(c.Pos.x - node.x) <= c.Dx
                    && Mathf.Abs(c.Pos.z - node.y) <= c.Dy)
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
                    if (force || !nodes[i].entered)
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
