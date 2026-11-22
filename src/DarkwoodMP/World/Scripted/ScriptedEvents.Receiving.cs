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
        // ---- receiving ------------------------------------------------------

        private void OnFired(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            FiredEvent fired = EventFiredMessage.Read(r);
            EventKind kind = fired.Kind;
            var obj = ObjRef.RefOf(fired.Trigger);
            int index = fired.Index;
            string name = fired.Name;

            if (s.IsHost)
            {
                var takes = FiredEventRules.HostTakes(kind);
                if (takes.Refused)
                {
                    _coop.Authority.Refuse(takes);
                    return;
                }
            }

            if (kind == EventKind.WorldTrigger
                || kind == EventKind.WorldTriggerCaused)
            {
                // Only the host's world sends these; a joiner runs the shared
                // part (and the messages, if near), and the joiner who caused
                // it (kind 6) its own part too.

                bool ours = kind == EventKind.WorldTriggerCaused;
                var et = ObjRef.Resolve<EventTriggers>(obj);
                var t = TriggerAt(et, index);
                if (Debug || ours)
                {
                    string caused = "";
                    if (ours)
                    {
                        caused = " (we caused it: our part too)";
                    }

                    string line =
                        $"[world] got world trigger {obj} #{index}{caused}: ";
                    string found = "not found here";
                    if (t != null)
                    {
                        found = $"{et.name} ({t.type})";
                    }

                    Plugin.Log.LogInfo(line + found);
                }

                if (t == null)
                {
                    var triggerPart = FiredEventRules.WorldTriggerPart(
                        ours, near: false);
                    RunWorldEvents(
                        name, triggerPart, replaying: true,
                        $"world trigger {obj} #{index}");
                    return;
                }

                if (t.fired && !t.multipleFire)
                {
                    return;
                }

                t.fired = true;
                t.firedExit = false;
                if (ours)
                {
                    HadOurPart(t);
                }

                var ge = t.gameEvents;
                if (CanRun(ge))
                {
                    bool near = NearTrigger(et);
                    try
                    {
                        var eventPart = FiredEventRules.WorldTriggerPart(
                            ours, near);
                        Parts.RunAs(eventPart, replaying: true, ge.fire);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't run world trigger {et.name} #{index}: "
                                + $"{e.Message}");
                    }
                }

                return;
            }

            if (kind == EventKind.PlayerPart)
            {
                // Only a request to the host; nothing runs here and it isn't
                // passed on.
                if (s.IsHost && _coop.Authority.Handling)
                {
                    var pet = ObjRef.Resolve<EventTriggers>(obj);
                    OnPlayerPart(from, pet, TriggerAt(pet, index));
                }

                return;
            }

            if (kind == EventKind.TriggerExit)
            {
                var et = ObjRef.Resolve<EventTriggers>(obj);
                var t = TriggerAt(et, index);
                if (s.IsHost && t != null && !ExitAllowed(from, et, t))
                {
                    return;
                }

                var wasBy = Parts.By;
                if (s.IsHost)
                {
                    Parts.By = from;
                }

                try
                {
                    EventPart exitPart;
                    if (s.IsHost)
                    {
                        exitPart = EventPart.HostAndShared;
                    }
                    else
                    {
                        exitPart = EventPart.SharedOnly;
                    }

                    ExitArrived(et, t, index, exitPart, replaying: s.IsClient);
                }
                finally
                {
                    Parts.By = wasBy;
                }
            }
            else if (kind == EventKind.Trigger)
            {
                var et = ObjRef.Resolve<EventTriggers>(obj);
                var t = TriggerAt(et, index);
                if (s.IsHost)
                {
                    // A trigger the host doesn't have (in a separate location
                    // only the joiner has loaded) is still the joiner's to use,
                    // if the world events it fires are ones the host has: the
                    // joiner's trip is its own, and the world events are what a
                    // joiner may ask for by name anyway (kind 2).
                    var authority = _coop.Authority;
                    bool away = authority.RequesterAway;
                    bool known = t != null || away || WorldEventsKnown(name);
                    var answer = FiredEventRules.AnswerTrigger(
                        HostFactsOf(from, t, et), away, known);
                    if (answer.Alone)
                    {
                        authority.AcceptAway(
                            $"event {obj} #{index}", FiredEventRules.Alone);
                        return;
                    }

                    authority.Refuse(answer.Verdict);
                    if (answer.Verdict.Refused)
                    {
                        return;
                    }

                    if (t != null)
                    {
                        // their own part runs in their game
                        GiveOwed(from, t);
                        ExpectTrips(from, t.gameEvents);
                    }
                }

                if (Debug)
                {
                    string line = $"[world] got event {obj} #{index}: ";
                    string found = "not found here";
                    if (t != null)
                    {
                        found = et.name;
                    }

                    Plugin.Log.LogInfo(line + found);
                }

                var wasBy = Parts.By;
                if (s.IsHost)
                {
                    Parts.By = from;
                }

                try
                {
                    EventPart part;
                    if (s.IsHost)
                    {
                        part = EventPart.HostAndShared;
                    }
                    else
                    {
                        part = EventPart.SharedOnly;
                    }

                    if (t == null)
                    {
                        RunWorldEvents(
                            name, part, replaying: s.IsClient,
                            $"event {obj} #{index}");
                    }
                    else
                    {
                        t.fired = true;
                        t.firedExit = false;
                        var ge = t.gameEvents;
                        if (CanRun(ge))
                        {
                            try
                            {
                                Parts.RunAs(
                                    part, replaying: s.IsClient, ge.fire);
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.LogWarning(
                                    $"Couldn't run event {et.name} #{index}: "
                                        + $"{e.Message}");
                            }

                            Owe(t, $"{et.name} #{index}");
                        }
                    }
                }
                finally
                {
                    Parts.By = wasBy;
                }
            }
            else
            {
                var events = Singleton<Events>.Instance;
                GameEvents ge = null;
                bool known = events != null
                    && events.worldEvents.TryGetValue(name, out ge)
                    && ge != null;
                if (s.IsHost)
                {
                    var authority = _coop.Authority;
                    var answer = FiredEventRules.AnswerWorldEvent(
                        FactsOf(ge), name, authority.RequesterAway);
                    if (answer.Alone)
                    {
                        authority.AcceptAway(
                            $"world event {name}", FiredEventRules.Alone);
                        return;
                    }

                    authority.Refuse(answer.Verdict);
                    if (answer.Verdict.Refused)
                    {
                        return;
                    }

                    ExpectTrips(from, ge);
                }

                if (Debug)
                {
                    string foundNote = ": not found here";
                    if (known)
                    {
                        foundNote = "";
                    }

                    Plugin.Log.LogInfo(
                        $"[world] got world event {name}" + foundNote);
                }

                if (!known)
                {
                    return;
                }

                var wasBy = Parts.By;
                if (s.IsHost)
                {
                    Parts.By = from;
                }

                try
                {
                    EventPart worldPart;
                    if (s.IsHost)
                    {
                        worldPart = EventPart.HostAndShared;
                    }
                    else
                    {
                        worldPart = EventPart.SharedOnly;
                    }

                    Parts.RunAs(
                        worldPart, replaying: s.IsClient,
                        () => events.fireWorldEvent(name));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't run world event {name}: {e.Message}");
                }
                finally
                {
                    Parts.By = wasBy;
                }
            }

            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EventFired);
                EventFiredMessage.Write(w, new FiredEvent
                {
                    Kind = kind,
                    Trigger = ObjRef.NameOf(obj),
                    Index = index,
                    Name = name,
                });

                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        private global::EventTrigger TriggerAt(
            EventTriggers et, int index)
        {
            if (et != null && index < et.eventTriggers.Count)
            {
                return et.eventTriggers[index];
            }

            return null;
        }

        /// <summary>
        /// Whether a trigger's GameEvents may run now: not run yet (or run each
        /// time), and active or allowed to run inactive.
        /// </summary>
        private bool CanRun(GameEvents ge) =>
            FiredEventRules.CanRun(FactsOf(ge));

        /// <summary>
        /// What the rules need to know of a GameEvents (ge may be null).
        /// </summary>
        internal EventsFacts FactsOf(GameEvents ge)
        {
            if (ge == null)
            {
                return default;
            }

            return new EventsFacts
            {
                Exists = true,
                Fired = ge.fired,
                MultipleFire = ge.multipleFire,
                Active = ge.gameObject.activeInHierarchy,
                RunsInactive = !ge.dontRunIfInactive,
            };
        }

        /// <summary>
        /// Requirements the host can check against its own world. The others
        /// are about the player who set it off (what they carry, their state),
        /// or are flags and fired events that reach the host a moment later.
        /// </summary>
        private static readonly
            HashSet<EventTriggerRequirement.Type> HostChecks =
                new HashSet<EventTriggerRequirement.Type>
            {
                EventTriggerRequirement.Type.itemState,
                EventTriggerRequirement.Type.itemTurnedOn,
                EventTriggerRequirement.Type.npcStateIsDead,
            };

        /// <summary>
        /// Client: whether our player is near enough a world trigger to see
        /// its messages (FiredEventRules.NearTrigger).
        /// </summary>
        private bool NearTrigger(EventTriggers et)
        {
            var p = Player.Instance;
            if (p == null)
            {
                return FiredEventRules.NearTrigger(
                    false, default, default);
            }

            return FiredEventRules.NearTrigger(
                true, Vec3Of(p.transform.position),
                Vec3Of(et.transform.position));
        }

        private static Vec3 Vec3Of(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Host: a joiner's player can have left this area trigger: it's an
        /// area here and they are near it.
        /// </summary>
        private bool ExitAllowed(
            ulong from, EventTriggers et, global::EventTrigger t)
        {
            var verdict = TriggerRules.MayExit(HostFactsOf(from, t, et));
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// The trigger can fire in the host's world and the requester is near
        /// it.
        /// </summary>
        private bool TriggerAllowed(
            ulong from, EventTriggers et, global::EventTrigger t)
        {
            var verdict = TriggerRules.MayFire(HostFactsOf(from, t, et));
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }
    }
}
