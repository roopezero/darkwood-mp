using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Scripted
{
    internal sealed partial class ScriptedEvents
    {
        /// <summary>
        /// Our player set off this trigger and its GameEvents are about to run:
        /// a joiner asks the host and runs its own and the shared part; the
        /// host tells the joiners to run the shared part.
        /// </summary>
        /// <returns>False if it couldn't be named (nothing was sent).</returns>
        internal bool SendTrigger(
            EventTriggers et, int index, bool world, Action accepted)
        {
            // In a dream of our own (or a place the host doesn't run): the host
            // hasn't got it, so it runs here alone.
            if (et == null)
            {
                // gone already (a trap taken away by its disarm): nothing to
                // name it by
                return false;
            }

            if (WorldActions.PlayerOwn(et.gameObject))
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] event {et.name} #{index}: ours alone, run "
                            + "here");
                }

                // A later part of the epilogue's: its shared steps run in
                // the others' games too (SendOwnTrigger).
                if (!world && WorldActions.InLaterPart(et.gameObject))
                {
                    SendOwnTrigger(et, index);
                }

                return false;
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return false;
            }

            FlushPosition();
            EventKind kind;
            if (world)
            {
                kind = EventKind.WorldTrigger;
            }
            else
            {
                kind = EventKind.Trigger;
            }

            var w = S.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = kind,
                Trigger = ObjRef.NameOf(r),
                Index = index,
                Name = WorldEventsOf(TriggerAt(et, index)),
            });

            string text = "event";
            if (world)
            {
                text = "world trigger";
            }

            Send(w, $"{text} {et.name} #{index} {r}", accepted);
            return true;
        }

        /// <summary>
        /// Our player left an area trigger and its exit events
        /// (EventTrigger.gameEventsExit) ran here: tell the others. The
        /// trigger's enter events are shared (their sounds, objects shown or
        /// hidden ran in every game), so their exit has to be too: a place's
        /// whispers (a looping sound step, stopped by the exit's sound step)
        /// went on for good in the games of everyone but the player who walked
        /// out. A joiner sends it as a request, but its own exit has already
        /// run (its own and the shared part; the host's part runs on the host):
        /// the leaver has left whatever the host says, and a refusal only means
        /// the others don't run it. The host runs the host's and the shared
        /// part and passes it on; each game runs it unless its own player is
        /// still in that area (ExitArrived).
        /// </summary>
        internal void SendExit(EventTriggers et, int index)
        {
            if (WorldActions.PlayerOwn(et.gameObject))
            {
                return; // nobody else has it, or it's our player's own
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return;
            }

            FlushPosition();
            var w = S.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.TriggerExit,
                Trigger = ObjRef.NameOf(r),
                Index = index,
                Name = "",
            });

            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent the exit of {et.name} #{index} {r}");
            }

            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            string what = $"exit of {et.name} #{index} {r}";
            _coop.Authority.Request(w, what, (ok, reason, _) =>
            {
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] the host didn't take the {what} ({reason}); "
                            + "it ran here");
                }
            }, inventory: false);
        }

        /// <summary>
        /// Another player left an area trigger (kind 4): run its exit events in
        /// the given part, as EventTrigger.fireExit does, once per entry
        /// (firedExit). Not while our own player is still in the area: the
        /// place goes on around them (its whispers keep playing), and their own
        /// exit runs it as they leave.
        /// </summary>
        private void ExitArrived(
            EventTriggers et, global::EventTrigger t, int index, EventPart part,
            bool replaying)
        {
            var arrival = FiredEventRules.OnExit(
                FactsOf(t), OurPlayerInside(et));
            if (arrival == ExitArrival.Ignored)
            {
                return;
            }

            if (arrival == ExitArrival.RanAlready)
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] exit of {et.name} #{index}: it already ran "
                            + "here");
                }

                return;
            }

            if (arrival == ExitArrival.StillInside)
            {
                Plugin.Log.LogInfo(
                    $"[world] exit of {et.name} #{index}: our player is still "
                        + "in it, it runs when they leave");
                return;
            }

            t.firedExit = true;
            if (arrival == ExitArrival.RunAndRearm)
            {
                t.fired = false;
            }

            var ge = t.gameEventsExit;
            if (Debug)
            {
                string eventName = "nothing";
                if (ge != null)
                {
                    eventName = ge.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got the exit of {et.name} #{index}: running "
                        + $"{eventName}");
            }

            if (ge == null || !CanRun(ge))
            {
                return;
            }

            try
            {
                Parts.RunAs(part, replaying, ge.fire);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't run the exit of {et.name} #{index}: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// Whether our player is in this area trigger, as
        /// EventTriggers.OnTriggerExit tells (entered and not left, and
        /// standing on its collider).
        /// </summary>
        internal static bool OurPlayerInside(EventTriggers et)
        {
            var p = Player.Instance;
            if (p == null || et == null || et.entered <= et.exited)
            {
                return false;
            }

            return Helpers.isComponentAtPos(
                p.transform.position, 1 << et.gameObject.layer, et);
        }

        /// <summary>
        /// The world events a trigger's steps fire by name (fireEvent with
        /// activeModifier: Events.fireWorldEvent), one per line, sent with the
        /// trigger. A game that can't find the trigger still runs these. The
        /// trigger may be in a separate location, which is in a game only once
        /// its player has been there: the hole in the chapter 1 village's fence
        /// (in the village's scene) fires openVillageShortcut, which opens the
        /// farm side's hole in the world, and a player who had never been to
        /// the village never got it open.
        /// </summary>
        internal string WorldEventsOf(global::EventTrigger t)
        {
            GameEvents ge = null;
            if (t != null)
            {
                ge = t.gameEvents;
            }

            if (ge == null || ge.events == null)
            {
                return "";
            }

            return string.Join(
                "\n", ge.events.Where(
                    e => e != null && !e.disabled
                        && e.type == GameEvent.Type.fireEvent
                        && e.activeModifier
                        && !string.IsNullOrEmpty(e.Value)).Select(e => e.Value)
                    .Distinct());
        }

        private static string[] Names(string list)
        {
            if (string.IsNullOrEmpty(list))
            {
                return new string[0];
            }

            return list.Split('\n');
        }

        /// <summary>
        /// Whether every world event named is one this game has (and there is
        /// at least one).
        /// </summary>
        private bool WorldEventsKnown(string list)
        {
            var events = Singleton<Events>.Instance;
            var names = Names(list);
            return events != null && names.Length > 0
                && names.All(
                    n => events.worldEvents.TryGetValue(n, out var ge)
                        && ge != null);
        }

        /// <summary>
        /// For a trigger this game doesn't have: run the world events it fires,
        /// in the given part.
        /// </summary>
        private void RunWorldEvents(
            string list, EventPart part, bool replaying, string what)
        {
            var events = Singleton<Events>.Instance;
            if (events == null)
            {
                return;
            }

            foreach (var n in Names(list))
            {
                if (!events.worldEvents.ContainsKey(n))
                {
                    continue;
                }

                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what} isn't here: running its world event "
                            + $"{n}");
                }

                try
                {
                    Parts.RunAs(
                        part, replaying, () => events.fireWorldEvent(n));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't run world event {n}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// True while the host runs a world event it has just sent as a
        /// character's animation's (FromAnimation).
        /// </summary>
        internal bool AnimationSent;

        /// <summary>
        /// A character's animation frame fires a world event
        /// (AnimationTriggerListener.checkFrameTrigger, a tk2d frame trigger of
        /// type fireWorldEvent). The runaway musician's hiding animation by the
        /// tractor does this: its frames fire musician_removeCollider and
        /// musician_setUnderTractor, and the latter switches on the tractor's
        /// own NPC (tractor_muzyk, NPC.disabled off), the one players then talk
        /// to. The runaway musician is a plain Character, so in a joiner's game
        /// it is a frozen copy of the host's, with its animation events cut
        /// (NetEnemies.Freeze): the event never ran there and the tractor's NPC
        /// stayed switched off, so a joiner couldn't talk to him.
        ///
        /// It's the world acting by itself, so it's the host's: the host runs
        /// it and sends it (EventFired kind 2, as a dialogue's world event
        /// goes), and the joiners run its shared part. A joiner's own
        /// characters leave theirs to the host (the host's copy plays the same
        /// animation, as the host's world runs around every player), except in
        /// an area only the joiner is in. The players' own animations (Player)
        /// are left as they were.
        /// </summary>
        /// <returns>Whether the game's own call runs here.</returns>
        internal bool FromAnimation(Component from, string name)
        {
            var s = S;
            var act = WorldEventRules.FromAnimation(AnimationOf(from, name));
            if (act == AnimationEvent.Run)
            {
                return true;
            }

            if (act == AnimationEvent.LeaveToHost)
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] left world event {name} from {from.name}'s "
                            + "animation to the host");
                }

                return false;
            }

            var w = s.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.WorldEvent,
                Trigger = default(ObjName),
                Index = 0,
                Name = name,
            });

            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent world event {name} from {from.name}'s "
                        + "animation");
            }

            s.Broadcast(w, Channel.Control, reliable: true);
            AnimationSent = true;
            return true;
        }

        /// <summary>
        /// What WorldEventRules.FromAnimation needs to know of a world event
        /// a character's animation fires.
        /// </summary>
        private AnimationFacts AnimationOf(Component from, string name)
        {
            var s = S;
            if (s == null)
            {
                return default;
            }

            bool character = from != null
                && from.GetComponent<Character>() != null
                && from.GetComponent<Player>() == null;
            var ge = WorldEventOf(Singleton<Events>.Instance, name);
            bool localOnly = s.IsClient && character && ge != null
                && Combat.NetEnemies.LocalOnly(from.gameObject);
            return new AnimationFacts
            {
                Active = s.Active,
                InWorld = WorldShare.InWorld,
                Current = Parts.Current,
                Replaying = Coop.Guards.Replaying,
                Character = character,
                Known = ge != null,
                Client = s.IsClient,
                LocalOnly = localOnly,
                Peers = s.Peers.Count,
                Fired = ge != null && ge.fired,
                MultipleFire = ge != null && ge.multipleFire,
            };
        }

        /// <summary>
        /// The world event of that name in events, or null.
        /// </summary>
        private static GameEvents WorldEventOf(Events events, string name)
        {
            if (events == null || string.IsNullOrEmpty(name)
                || !events.worldEvents.TryGetValue(name, out var ge))
            {
                return null;
            }

            return ge;
        }

        /// <summary>
        /// A world event fired by name (Events.fireWorldEvent): share it if
        /// our player set it off (a dialogue's outcome).
        /// </summary>
        /// <returns>Whether the game's own call runs here.</returns>
        internal bool OnWorldEvent(Events events, string name)
        {
            var ge = WorldEventOf(events, name);
            var facts = new WorldEventFacts
            {
                Current = Parts.Current,
                Replaying = Coop.Guards.Replaying,
                Sharing = Coop.Guards.Sharing,
                AnimationSent = AnimationSent,
                Pan = name == Story.EpilogueSync.PanEvent,
                Known = ge != null,
                Fired = ge != null && ge.fired,
                MultipleFire = ge != null && ge.multipleFire,
            };

            if (!WorldEventRules.MaySend(facts))
            {
                return true;
            }

            bool ours = WorldActions.ByLocalPlayer()
                || OnStack(typeof(DialogueWindow));
            if (!ours)
            {
                return true;
            }

            // A dream NPC's outcome in a dream of our own, or anything in a
            // later part of the epilogue (each game builds its own): it stays
            // ours alone, as in single player.
            bool later = WorldActions.InLaterPart(null);
            bool ownArea = WorldActions.InOwnArea() || later;
            if (!WorldEventRules.Sends(ours, ownArea))
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] world event {name}: ours alone, run here");
                }

                // A later part's: its shared steps run in the others' games
                // too, left unfired there (SendOwnWorldEvent).
                if (later && ge != null)
                {
                    SendOwnWorldEvent(name);
                }

                return true;
            }

            SendWorldEvent(name, () =>
            {
                if (events != null)
                {
                    events.fireWorldEvent(name);
                }
            });

            // A joiner runs it (its own and the shared part) once the host says
            // yes.
            return !S.IsClient;
        }

        internal void SendWorldEvent(string name, Action accepted)
        {
            var w = S.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.WorldEvent,
                Trigger = default(ObjName),
                Index = 0,
                Name = name,
            });

            Send(w, $"world event {name}", accepted);
        }

        /// <summary>
        /// The host tells everyone; a joiner asks, and runs accepted (its own
        /// and the shared part) on a yes.
        /// </summary>
        private void Send(PacketWriter w, string what, Action accepted)
        {
            if (Debug)
            {
                Plugin.Log.LogInfo($"[world] sent {what}");
            }

            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            _coop.Authority.Request(w, what, (ok, reason, result) =>
            {
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what} not run here: the host refused it "
                            + $"({reason})");
                    return;
                }

                var part = FiredEventRules.PartAfterYes(result);
                if (part == EventPart.All)
                {
                    // We were somewhere the host doesn't run
                    // (Authority.AcceptAway): every part of it is ours.
                    Plugin.Log.LogInfo(
                        $"[world] {what}: ours alone, the host says; running "
                            + "all of it here");
                }
                else if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what}: the host agreed, running our part "
                            + "and the shared part");
                }

                try
                {
                    Parts.RunAs(part, replaying: false, accepted);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Couldn't run {what}: {e.Message}");
                }
            }, inventory: false);
        }
    }
}
