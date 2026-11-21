using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DarkwoodMP.Net;
using DarkwoodMP.World.Scripted;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    public static partial class ScriptedEvents
    {
        // ---- receiving ------------------------------------------------------

        private static void OnFired(ulong from, PacketReader r)
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

            if (kind == EventKind.WorldTrigger
                || kind == EventKind.WorldTriggerCaused)
            {
                // Only the host's world sends these; a joiner runs the shared
                // part (and the messages, if near), and the joiner who caused
                // it (kind 6) its own part too.
                if (s.IsHost)
                {
                    Coop.Current.Authority.Refuse(
                        "the world's own events are the host's");
                    return;
                }

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
                    Part triggerPart;
                    if (ours)
                    {
                        triggerPart = Part.PlayerAndShared;
                    }
                    else
                    {
                        triggerPart = Part.SharedOnly;
                    }

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
                    var p = Player.Instance;
                    bool near = p != null
                        && (p.transform.position
                            - et.transform.position).sqrMagnitude
                            < NearMessages * NearMessages;
                    try
                    {
                        Part eventPart;
                        if (ours)
                        {
                            eventPart = Part.PlayerAndShared;
                        }
                        else if (near)
                        {
                            eventPart = Part.SharedAndMessages;
                        }
                        else
                        {
                            eventPart = Part.SharedOnly;
                        }

                        RunAs(eventPart, replaying: true, ge.fire);
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
                if (s.IsHost && Coop.Current.Authority.Handling)
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

                var wasBy = By;
                if (s.IsHost)
                {
                    By = from;
                }

                try
                {
                    Part exitPart;
                    if (s.IsHost)
                    {
                        exitPart = Part.HostAndShared;
                    }
                    else
                    {
                        exitPart = Part.SharedOnly;
                    }

                    ExitArrived(et, t, index, exitPart, replaying: s.IsClient);
                }
                finally
                {
                    By = wasBy;
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
                    if (t == null && Coop.Current.Authority.RequesterAway)
                    {
                        Coop.Current.Authority.AcceptAway(
                            $"event {obj} #{index}", Alone);
                        return;
                    }

                    if (t == null && !WorldEventsKnown(name))
                    {
                        Coop.Current.Authority.Refuse("no such event here");
                        return;
                    }

                    if (t != null && !TriggerAllowed(from, et, t))
                    {
                        return;
                    }

                    if (t != null)
                    {
                        // their own part runs in their game
                        GiveOwed(from, t);
                    }

                    if (t != null)
                    {
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

                var wasBy = By;
                if (s.IsHost)
                {
                    By = from;
                }

                try
                {
                    Part part;
                    if (s.IsHost)
                    {
                        part = Part.HostAndShared;
                    }
                    else
                    {
                        part = Part.SharedOnly;
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
                                RunAs(part, replaying: s.IsClient, ge.fire);
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
                    By = wasBy;
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
                    if (!known && Coop.Current.Authority.RequesterAway)
                    {
                        Coop.Current.Authority.AcceptAway(
                            $"world event {name}", Alone);
                        return;
                    }

                    if (!known)
                    {
                        Coop.Current.Authority.Refuse(
                            $"no world event {name} here");
                        return;
                    }

                    if (ge.fired && !ge.multipleFire)
                    {
                        Coop.Current.Authority.Refuse(
                            $"{name} has already happened");
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

                var wasBy = By;
                if (s.IsHost)
                {
                    By = from;
                }

                try
                {
                    Part worldPart;
                    if (s.IsHost)
                    {
                        worldPart = Part.HostAndShared;
                    }
                    else
                    {
                        worldPart = Part.SharedOnly;
                    }

                    RunAs(
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
                    By = wasBy;
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

        private static global::EventTrigger TriggerAt(
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
        private static bool CanRun(GameEvents ge) =>
            FiredEventRules.CanRun(FactsOf(ge));

        /// <summary>
        /// What the rules need to know of a GameEvents (ge may be null).
        /// </summary>
        internal static EventsFacts FactsOf(GameEvents ge)
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
        /// Host: a joiner's player can have left this area trigger: it's an
        /// area here and they are near it.
        /// </summary>
        private static bool ExitAllowed(
            ulong from, EventTriggers et, global::EventTrigger t)
        {
            var verdict = TriggerRules.MayExit(HostFactsOf(from, t, et));
            Coop.Current.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// The trigger can fire in the host's world and the requester is near
        /// it.
        /// </summary>
        private static bool TriggerAllowed(
            ulong from, EventTriggers et, global::EventTrigger t)
        {
            var verdict = TriggerRules.MayFire(HostFactsOf(from, t, et));
            Coop.Current.Authority.Refuse(verdict);
            return !verdict.Refused;
        }
    }
}
