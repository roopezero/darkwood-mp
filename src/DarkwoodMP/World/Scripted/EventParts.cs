using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DarkwoodMP.Net;
using DarkwoodMP.World.Hideout;
using UnityEngine;
using Chapters = DarkwoodMP.World.Story.Chapters;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// Which part of a scripted event runs now (ScriptedEvents' class
    /// comment), carried into the coroutines the event starts. It lives as
    /// long as the game (ModHost.Parts): a coroutine started in a session
    /// can still be running after it ends, and the count of running step
    /// coroutines (StepDepth) has to stay right across sessions, so the
    /// coroutine patches keep it also in a game played alone.
    /// </summary>
    internal sealed class EventParts : IDisposable
    {

        /// <summary>
        /// Steps that change only how a place looks and sounds to whoever
        /// stands in it. A place's area trigger that fires each time a player
        /// walks in (and its exit each time they walk out) uses them for its
        /// mood: the Dry Meadow's markings (gridObj_signs, area_signs_01) fade
        /// in spot_black and TINT, sprites over the whole screen, and start
        /// their particles; others start 2D whispers. Run in every game, as
        /// shared steps, one player walking in blacked out the screen of every
        /// other player wherever they were, until the last player inside walked
        /// out (and a player stepping back over the edge blacked out those who
        /// had left). In such a trigger these are the player's own: only the
        /// game of the player who walked in (or out) runs them.
        private static readonly HashSet<GameEvent.Type> LookSteps =
            new HashSet<GameEvent.Type>
        {
            GameEvent.Type.tweenColor, GameEvent.Type.particle,
            GameEvent.Type.sound, GameEvent.Type.changeIdleSound,
        };

        /// <summary>
        /// runFunction steps of that kind: a particle system's emission
        /// switched on or off.
        /// </summary>
        private static readonly HashSet<string> LookFunctions =
            new HashSet<string> { "resumeEmission", "stopEmission" };

        private readonly ConditionalWeakTable<object, Started> _coroutines =
            new ConditionalWeakTable<object, Started>();

        /// <summary>
        /// The GameEvents that a repeatable area trigger runs on enter or
        /// exit (built when needed).
        /// </summary>
        private HashSet<GameEvents> _areaEvents;

        private bool _areasDirty = true;

        public EventParts()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>
        /// The part being run now (set around a GameEvents.fire, and inside
        /// the coroutines it started).
        /// </summary>
        public EventPart Current { get; set; } = EventPart.All;

        /// <summary>
        /// Host: the joiner whose event is being run now (the peer that sent
        /// the EventFired), or 0. Carried into the coroutines the event
        /// starts, as Current is (World.NpcFollowers uses it to know who
        /// recruited a follower).
        /// </summary>
        public ulong By { get; set; }

        /// <summary>
        /// The trigger whose coroutine is running now (EventTrigger.fire's
        /// body), if it's one to share.
        /// </summary>
        public Started Firing { get; set; }

        /// <summary>
        /// How many GameEvent step coroutines are running right now (a
        /// trigger set off inside one is a chain).
        /// </summary>
        public int StepDepth { get; set; }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        /// <summary>
        /// Keep what this coroutine was started with, for each of its steps.
        /// </summary>
        public void Remember(object coroutine, Started st)
        {
            _coroutines.Remove(coroutine);
            _coroutines.Add(coroutine, st);
        }

        /// <summary>What this coroutine was started with, if kept.</summary>
        public bool StartedWith(object coroutine, out Started st)
        {
            return _coroutines.TryGetValue(coroutine, out st);
        }

        /// <summary>
        /// GameEvent.fire: whether this step runs in the part being run now;
        /// a step that doesn't gets an empty coroutine (instead). A new
        /// chapter and the game's end are the host's, whoever set them off
        /// (see World.Chapters).
        /// </summary>
        public bool StepStarts(
            GameEvent ge, GameObject thisGO, out IEnumerator instead)
        {
            instead = null;
            bool runs;
            if (Chapters.IsStoryStep(ge))
            {
                runs = Chapters.StoryStepRuns(ge, thisGO);
            }
            else
            {
                runs = Runs(ge, thisGO);
            }

            if (!runs)
            {
                instead = ScriptedEvents.Nothing();
            }

            return runs;
        }

        /// <summary>
        /// GameEvent.fire started this step's coroutine: it keeps the part
        /// being run now. Returns the coroutine to run.
        /// </summary>
        public IEnumerator StepStarted(GameEvent ge, IEnumerator coroutine)
        {
            if (coroutine == null)
            {
                return null;
            }

            if (Current != EventPart.All)
            {
                Remember(coroutine, new Started { Part = Current, By = By });
            }

            // The host sends what a world move left in each container (the
            // coroutine above keeps its part).
            if (Hideouts.Together && Session.Instance.IsHost
                && Hideouts.IsWorldMove(ge))
            {
                return Hideouts.SendAfter(coroutine, ge);
            }

            return coroutine;
        }

        /// <summary>
        /// A step of an EventTrigger.fire or GameEvent.fire coroutine starts:
        /// it runs in the part it was started with. Returns what to put back
        /// after the step (LeaveStep).
        /// </summary>
        public StepSaved EnterStep(object coroutine)
        {
            var saved = new StepSaved
            {
                Part = Current,
                Firing = Firing,
                By = By,
            };

            if (coroutine.GetType().DeclaringType == typeof(GameEvent))
            {
                StepDepth++;
            }

            if (!StartedWith(coroutine, out var st))
            {
                return saved;
            }

            saved.Kept = true;
            Current = st.Part;
            if (st.ByPlayer)
            {
                Firing = st;
            }
            else
            {
                Firing = null;
            }

            By = st.By;
            return saved;
        }

        /// <summary>The step that EnterStep started has run.</summary>
        public void LeaveStep(object coroutine, StepSaved saved)
        {
            if (coroutine.GetType().DeclaringType == typeof(GameEvent)
                && StepDepth > 0)
            {
                StepDepth--;
            }

            if (saved.Kept)
            {
                Current = saved.Part;
                Firing = saved.Firing;
                By = saved.By;
            }
        }

        /// <summary>
        /// Put Current back to what it was, if it was set (Set).
        /// </summary>
        public void PutBack(PartSaved saved)
        {
            if (saved.Set)
            {
                Current = saved.Was;
            }
        }

        /// <summary>
        /// Run something with Current set to part (and Replaying, for a
        /// change that came from another game).
        /// </summary>
        public void RunAs(EventPart part, bool replaying, Action a)
        {
            var was = Current;
            bool wasReplaying = Coop.Guards.Replaying;
            Current = part;
            if (replaying)
            {
                Coop.Guards.Replaying = true;
            }

            try
            {
                a();
            }
            finally
            {
                Current = was;
                Coop.Guards.Replaying = wasReplaying;
            }
        }

        /// <summary>
        /// Whether this step runs in the part being run now
        /// (StepRules.Runs). With nothing shared going on (EventPart.All)
        /// every step runs, so its facts aren't read.
        /// </summary>
        public bool Runs(GameEvent ge, GameObject thisGO)
        {
            if (Current == EventPart.All)
            {
                return true;
            }

            return StepRules.Runs(Current, FactsOf(ge, thisGO));
        }

        /// <summary>
        /// What StepRules needs to know about a step (thisGO: the object
        /// whose GameEvents it is in, or null).
        /// </summary>
        public StepFacts FactsOf(GameEvent ge, GameObject thisGO)
        {
            if (ge == null)
            {
                return default;
            }

            return new StepFacts
            {
                Exists = true,
                Disabled = ge.disabled,
                Type = ScriptedEvents.StepTypeOf(ge.type),
                ActiveModifier = ge.activeModifier,
                ActiveModifier2 = ge.activeModifier2,
                RelentlessPursuit = ge.relentlessPursuit,
                SpecialItem = ScriptedEvents.AddsSpecialItem(ge),
                TargetsPlayer = ScriptedEvents.TargetsPlayer(ge),
                PlayerType = ScriptedEvents.IsPlayerStep(ge.type),
                AreaLook = IsAreaLook(ge, thisGO),
                HostType = ScriptedEvents.IsHostStep(ge.type),
                WorldMove = Hideouts.IsWorldMove(ge),
                Message = ge.type == GameEvent.Type.displayMessage,
            };
        }

        /// <summary>
        /// Area triggers came or went (a scene loaded or unloaded, an
        /// EventTriggers woke up): look again.
        /// </summary>
        public void AreasChanged()
        {
            _areasDirty = true;
        }

        /// <summary>
        /// Whether this step only changes how a place looks or sounds, in a
        /// repeatable area trigger's events.
        /// </summary>
        public bool IsAreaLook(GameEvent ge, GameObject thisGO)
        {
            if (ge == null || thisGO == null)
            {
                return false;
            }

            if (!LookSteps.Contains(ge.type)
                && !(ge.type == GameEvent.Type.runFunction && ge.Value != null
                    && LookFunctions.Contains(ge.Value)))
            {
                return false;
            }

            if (_areasDirty || _areaEvents == null)
            {
                _areaEvents = AreaEvents();
                _areasDirty = false;
            }

            foreach (var g in thisGO.GetComponents<GameEvents>())
            {
                if (g != null && _areaEvents.Contains(g)
                    && g.events.Contains(ge))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<GameEvents> AreaEvents()
        {
            var set = new HashSet<GameEvents>();
            foreach (var et in UnityEngine.Object
                .FindObjectsOfType<EventTriggers>(true))
            {
                if (et == null || et.eventTriggers == null)
                {
                    continue;
                }

                foreach (var t in et.eventTriggers)
                {
                    if (t != null
                        && t.type == global::EventTrigger.Type.area
                        && t.multipleFire)
                    {
                        if (t.gameEvents != null)
                        {
                            set.Add(t.gameEvents);
                        }

                        if (t.gameEventsExit != null)
                        {
                            set.Add(t.gameEventsExit);
                        }
                    }
                }
            }

            return set;
        }

        /// <summary>
        /// What a coroutine's step changed, to put back after it.
        /// </summary>
        public struct StepSaved
        {
            public EventPart Part;

            public Started Firing;

            /// <summary>
            /// Whether the coroutine was started with a part (and Part,
            /// Firing and By are to be put back).
            /// </summary>
            public bool Kept;

            public ulong By;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AreasChanged();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            AreasChanged();
        }
    }
}
