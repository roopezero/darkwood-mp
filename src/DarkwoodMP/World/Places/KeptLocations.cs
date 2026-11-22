using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Places
{
    /// <summary>
    /// Host: the separate locations joiners are in keep running (entered) in
    /// our game even when our player is elsewhere, so our game runs their
    /// enemies. What happens is the core's HostLocationRules.
    /// </summary>
    internal sealed class KeptLocations
    {
        private readonly CoopSession _coop;

        public KeptLocations(CoopSession coop)
        {
            _coop = coop;
        }

        // ---- host: running a location for the joiners in it -----------------

        /// <summary>
        /// Host: separate locations kept entered (running) because a joiner is
        /// in them.
        /// </summary>
        private readonly HashSet<Location> _kept =
            new HashSet<Location>();
        /// <summary>
        /// Host: locations we entered for a joiner: their enter events are the
        /// joiner's game's to run, not ours.
        /// </summary>
        private readonly HashSet<Location> _quietEnter =
            new HashSet<Location>();
        private float _nextKeep;

        /// <summary>
        /// Host: whether we keep this location running for a joiner in it (our
        /// player may be elsewhere).
        /// </summary>
        public bool KeptForOthers(Location l) =>
            l != null && _kept.Contains(l);

        /// <summary>
        /// Host: the save records a location kept running only for a joiner
        /// as our player left it (entered false): entered says our own player
        /// went in (LocationPatches).
        /// </summary>
        internal void AfterSave(Location.SaveState state, Location location)
        {
            if (HostLocationRules.SavesAsLeft(
                KeptForOthers(location), Whereabouts.OursNow(location)))
            {
                state.entered = false;
            }
        }

        /// <summary>
        /// Kept locations whose loose objects DragSync has noted (once each
        /// time it starts being kept).
        /// </summary>
        private readonly HashSet<Location> _noted =
            new HashSet<Location>();

        /// <summary>
        /// Host, twice a second: the locations joiners are in are entered
        /// (their objects and characters switched on, as the game does when its
        /// player goes there) even when our player isn't there, so our game
        /// runs their enemies and NetEnemies sends them to those joiners. One
        /// nobody is in any more is left again, unless our player is in it.
        /// </summary>
        internal void KeepRunning(OutsideLocations o)
        {
            var now = new Seconds(Time.unscaledTime);
            if (!HostLocationRules.KeepDue(now, new Seconds(_nextKeep)))
            {
                return;
            }

            _nextKeep = HostLocationRules.NextKeep(now).Value;
            var want = new HashSet<Location>();
            Dreams dreams = Singleton<Dreams>.Instance;
            Location dream = null;
            if (dreams != null)
            {
                dream = dreams.dreamLocation;
            }

            foreach (var p in Combat.NetPlayerProxy.All.Values)
            {
                // the shared dream runs with our player in it
                Location there = null;
                if (p != null)
                {
                    there = p.InLocation;
                }

                if (HostLocationRules.Wanted(
                    p != null, there != null, there == dream))
                {
                    want.Add(there);
                }
            }

            foreach (var loc in want)
            {
                if (_kept.Add(loc))
                {
                    string place = Core.getTrueLocationName(loc.name);
                    string note = "";
                    if (loc.entered)
                    {
                        note = " (already entered)";
                    }

                    Plugin.Log.LogInfo(
                        $"[locations] keeping {place} running for the "
                            + $"joiner(s) in it{note}");
                }

                EnterStep step = HostLocationRules.EnterFor(
                    loc.entered, loc.gameObject.activeInHierarchy);
                if (step == EnterStep.ForceEnter)
                {
                    _quietEnter.Add(loc);
                    loc.enter(force: true);
                }
                else if (step == EnterStep.Enter)
                {
                    _quietEnter.Add(loc);
                    loc.enter(force: false);
                }

                if (_noted.Add(loc))
                {
                    _coop.Drags.NoteAll(loc.gameObject);
                }
            }

            _kept.RemoveWhere(l => l == null);
            _noted.RemoveWhere(l => l == null || !_kept.Contains(l));
            foreach (var loc in _kept.Where(l => !want.Contains(l)).ToList())
            {
                _kept.Remove(loc);
                _quietEnter.Remove(loc);
                string name = Core.getTrueLocationName(loc.name);
                bool ours = Whereabouts.OursNow(loc);
                string text = ": left";
                if (ours)
                {
                    text = "; our player is, so it stays";
                }

                Plugin.Log.LogInfo(
                    $"[locations] nobody else is in {name} any more{text}");
                if (!HostLocationRules.LeavesUnwanted(ours))
                {
                    continue;
                }

                loc.leave(force: true);
                WorldGrid worldGrid = Singleton<WorldGrid>.Instance;
                if (worldGrid != null)
                {
                    WorldGrid.Grid grid = worldGrid.getGrid(name);
                    if (grid != null)
                    {
                        grid.leave();
                    }
                }
            }
        }

        /// <summary>
        /// Host: Location.leave on a location kept for a joiner (our player
        /// going back to the world, or on to another location:
        /// OutsideLocations.leaveAllLocations) doesn't switch it off. Our
        /// player's exit events still run.
        /// </summary>
        internal bool BeforeLeave(Location loc, bool force)
        {
            LeaveStep step = HostLocationRules.BeforeLeave(LeaveOf(loc, force));
            if (step == LeaveStep.Leave)
            {
                _quietEnter.Remove(loc);
                return true;
            }

            if (step == LeaveStep.StayWithExitEvents)
            {
                CheckExitEvents(loc);
                loc.timeLeft = Singleton<Controller>.Instance.CurrentTimeAndDay;
                Plugin.Log.LogInfo(
                    "[locations] our player left "
                        + $"{Core.getTrueLocationName(loc.name)}; it keeps "
                        + "running for the joiner(s) in it");
            }

            return false;
        }

        private LeaveFacts LeaveOf(Location loc, bool force)
        {
            var d = Singleton<Dreams>.Instance;
            return new LeaveFacts
            {
                Kept = _kept.Contains(loc),
                Force = force,
                Entered = loc.entered,
                LoadingGame = Core.loadingGame,
                Dreaming = d != null && d.dreaming,
                WantToDream = d != null && d.wantToDream,
            };
        }

        /// <summary>
        /// Host: our player going into a location already entered for a joiner
        /// gets the enter events it would have had.
        /// </summary>
        internal void BeforeEnter(Location loc, bool force)
        {
            if (HostLocationRules.RunsEnterEvents(EnterOf(loc, force)))
            {
                CheckEnterEvents(loc);
            }
        }

        private EnterFacts EnterOf(Location loc, bool force)
        {
            return new EnterFacts
            {
                Force = force,
                Entered = loc.entered,
                Kept = _kept.Contains(loc),
                Quiet = _quietEnter.Contains(loc),
            };
        }

        /// <summary>
        /// Host: Location.checkEnterEvents for a location entered for a joiner
        /// is skipped (theirs to run).
        /// </summary>
        internal bool BeforeEnterEvents(Location loc) =>
            HostLocationRules.RunsGameEnterEvents(_quietEnter.Remove(loc));

        private static readonly MethodInfo ExitEvents =
            AccessTools.Method(typeof(Location), "checkExitEvents");
        private static readonly MethodInfo EnterEvents =
            AccessTools.Method(typeof(Location), "checkEnterEvents");
        private void CheckExitEvents(Location l)
        {
            try
            {
                MethodInfo exitEvents = ExitEvents;
                if (exitEvents != null)
                {
                    exitEvents.Invoke(l, null);
                }
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"[locations] {l.name}'s exit events: {message}");
            }
        }

        private void CheckEnterEvents(Location l)
        {
            try
            {
                MethodInfo enterEvents = EnterEvents;
                if (enterEvents != null)
                {
                    enterEvents.Invoke(l, null);
                }
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"[locations] {l.name}'s enter events: {message}");
            }
        }
    }
}
