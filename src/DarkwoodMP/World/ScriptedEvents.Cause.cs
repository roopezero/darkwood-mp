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
        // ---- sending --------------------------------------------------------

        // ---- who caused a world trigger -------------------------------------
        //
        // A world trigger can still have a player behind it: the one who killed
        // the NPC (onDeath), broke the item (an item's onDeath) or switched on
        // the machine whose ActionWhenTurnedOn keeps setting it off
        // (refreshWhenTurnedOn). Run whole on the host, its player part (an
        // item, a dialogue, a trip, a journal entry) went to the host's player
        // even when a joiner did it. Now the host runs the host's and the
        // shared part, the joiner who caused it its own and the shared part
        // (EventFired kind 6), and the others the shared part as for any world
        // trigger (kind 3). Without a player behind it (the clock, a saved
        // object appearing) it stays as it was.

        /// <summary>
        /// Host: the joiner whose action is being applied now (a hit, an NPC's
        /// death, an item broken or switched on), or 0.
        /// </summary>
        internal static ulong Cause;

        /// <summary>
        /// Host: who switched an item on, while it's on: a joiner's peer id
        /// (the host's own player isn't kept).
        /// </summary>
        private static readonly ConditionalWeakTable<Item, Switcher> Switchers =
            new ConditionalWeakTable<Item, Switcher>();
        private sealed class Switcher
        {
            public ulong Peer;
        }

        /// <summary>
        /// Host: an item was switched on, by Cause (a joiner's ItemOn) or by
        /// the host's own player or world.
        /// </summary>
        internal static void SwitchedOn(Item item)
        {
            var s = S;
            if (item == null || s == null || !s.IsHost)
            {
                return;
            }

            Switchers.Remove(item);
            if (Cause != 0)
            {
                Switchers.Add(item, new Switcher { Peer = Cause });
            }
        }

        /// <summary>Host: the joiner who switched this item on, or 0.</summary>
        internal static ulong SwitcherOf(Item item)
        {
            if (item != null && Switchers.TryGetValue(item, out var sw))
            {
                return sw.Peer;
            }

            return 0;
        }

        /// <summary>
        /// Host: the joiner behind a world trigger of this type set off now, or
        /// 0 (none, or they've gone).
        /// </summary>
        internal static ulong CauseFor(global::EventTrigger.Type type)
        {
            var s = S;
            if (Cause == 0 || s == null || !s.IsHost
                || !s.Peers.Contains(Cause))
            {
                return 0;
            }

            if (type == global::EventTrigger.Type.onDeath
                || type == global::EventTrigger.Type.refreshWhenTurnedOn)
            {
                return Cause;
            }

            return 0;
        }

        /// <summary>
        /// Host: a world trigger a joiner caused is about to run its GameEvents
        /// here. Tell that joiner to run its own and the shared part (kind 6),
        /// and the others the shared part (kind 3). False if it couldn't be
        /// named or the joiner has gone (then it runs as any world trigger).
        /// </summary>
        internal static bool SendCaused(
            EventTriggers et, int index, ulong cause)
        {
            var s = S;
            if (s == null || !s.IsHost || et == null
                || !s.Peers.Contains(cause))
            {
                return false;
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return false;
            }

            var t = TriggerAt(et, index);
            string names = WorldEventsOf(t);
            foreach (var kind in new[]
            {
                EventKind.WorldTrigger,
                EventKind.WorldTriggerCaused
            })
            {
                var w = s.Begin(MsgType.EventFired);
                EventFiredMessage.Write(w, new FiredEvent
                {
                    Kind = kind,
                    Trigger = ObjRef.NameOf(r),
                    Index = index,
                    Name = names,
                });

                if (kind == EventKind.WorldTrigger)
                {
                    s.Broadcast(
                        w, Channel.Control, reliable: true, except: cause);
                }
                else
                {
                    s.SendTo(cause, w, Channel.Control, reliable: true);
                }
            }

            if (t != null)
            {
                GiveOwed(cause, t);
                ExpectTrips(cause, t.gameEvents);
            }

            global::EventTrigger.Type? type = null;
            if (t != null)
            {
                type = t.type;
            }

            Plugin.Log.LogInfo(
                $"[world] world trigger {et.name} #{index} "
                    + $"{r} ({type}): {s.NameOf(cause)} caused it, so its "
                    + "player part is theirs");
            return true;
        }

        /// <summary>
        /// Client: tell the host where our player is now, ahead of a request it
        /// checks our reach for.
        /// </summary>
        private static void FlushPosition()
        {
            if (S != null && S.IsClient)
            {
                Sync.Players.PlayerSync instance = Plugin.Instance.Host.Players;
                if (instance != null)
                {
                    instance.FlushNow();
                }
            }
        }
    }
}
