using System;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// A player's own events in a later part of the epilogue (the flat, the
    /// crater: WorldActions.PlayerOwn). Each player goes through the
    /// once-only events there for themselves: the flat's wardrobe changes the
    /// clothes of whoever uses it, and its rooms light up for whoever walks
    /// in. But the same events also change the place (the wardrobe opens the
    /// kitchen's wall and the flat's doors), and that has to happen in every
    /// game, or a player who didn't go first is walled out. So the event runs
    /// whole in the doer's game, which sends it (EventFired kind 7 or 8); the
    /// other games run its shared steps only (the host also its own: doors),
    /// straight from its steps, so it isn't marked fired (nor destroyed, nor
    /// a dialogue's state used up) there, and each player still gets all of
    /// it when they set it off.
    /// </summary>
    internal sealed partial class ScriptedEvents
    {
        /// <summary>
        /// Our player's trigger in a later part ran whole here: the others
        /// run its shared steps.
        /// </summary>
        internal void SendOwnTrigger(EventTriggers et, int index)
        {
            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var w = S.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.OwnTrigger,
                Trigger = ObjRef.NameOf(r),
                Index = index,
                Name = "",
            });

            SendOwn(w, $"our own event {et.name} #{index} {r}");
        }

        /// <summary>
        /// Our player's world event in a later part (a dialogue's outcome)
        /// ran whole here: the others run its shared steps.
        /// </summary>
        internal void SendOwnWorldEvent(string name)
        {
            var w = S.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.OwnWorldEvent,
                Trigger = default(ObjName),
                Index = 0,
                Name = name,
            });

            SendOwn(w, $"our own world event {name}");
        }

        private void SendOwn(PacketWriter w, string what)
        {
            Plugin.Log.LogInfo(
                $"[world] {what} ran here (a later part of the epilogue): "
                    + "the others run its shared steps");
            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            // The host takes a client's EventFired only as a Request (sent
            // bare, it was dropped, so a joiner's wardrobe never opened the
            // host's kitchen). It already ran here, so a refusal has nothing
            // to roll back.
            _coop.Authority.Request(w, what, (ok, reason, _) =>
            {
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] the host didn't take {what} ({reason})");
                }
            }, inventory: false);
        }

        /// <summary>
        /// Another player's own event in a later part (kind 7 or 8): run its
        /// shared steps here if our player is in that part, and the host
        /// passes it on.
        /// </summary>
        private void OnOwn(ulong from, FiredEvent fired)
        {
            var s = S;
            GameEvents ge = OwnEventsOf(fired);
            bool known = ge != null;
            var facts = new OwnEventFacts
            {
                Host = s.IsHost,
                Known = known,
                InOurPart = known && WorldActions.InLaterPart(ge.gameObject),
            };

            EventPart? part = FiredEventRules.OwnPart(facts);
            Plugin.Log.LogInfo(
                $"[world] got another player's own event {fired.Kind} "
                    + $"{fired.Name}{ObjRef.RefOf(fired.Trigger)} "
                    + $"#{fired.Index}: {OwnNote(part)}");
            if (part.HasValue)
            {
                RunSteps(ge, part.Value, s.IsClient);
            }

            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EventFired);
                EventFiredMessage.Write(w, fired);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        private static string OwnNote(EventPart? part)
        {
            if (!part.HasValue)
            {
                return "not in our part, nothing run";
            }

            return $"running its steps ({part.Value}), left unfired";
        }

        private GameEvents OwnEventsOf(FiredEvent fired)
        {
            if (fired.Kind == EventKind.OwnWorldEvent)
            {
                var events = Singleton<Events>.Instance;
                GameEvents ge = null;
                if (events != null
                    && events.worldEvents.TryGetValue(fired.Name, out ge))
                {
                    return ge;
                }

                return null;
            }

            var et = ObjRef.Resolve<EventTriggers>(
                ObjRef.RefOf(fired.Trigger));
            var t = TriggerAt(et, fired.Index);
            if (t == null)
            {
                return null;
            }

            return t.gameEvents;
        }

        /// <summary>
        /// Start each of its steps in this part, as GameEvents.fire does, but
        /// without marking it fired, destroying it or telling its listeners.
        /// </summary>
        private void RunSteps(GameEvents ge, EventPart part, bool replaying)
        {
            if (ge == null || ge.events == null)
            {
                return;
            }

            var runner = Singleton<Controller>.Instance;
            try
            {
                Parts.RunAs(part, replaying, () =>
                {
                    foreach (var e in ge.events)
                    {
                        if (e != null)
                        {
                            runner.StartCoroutine(e.fire(ge.gameObject));
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't run the steps of {ge.name}: {e.Message}");
            }
        }
    }
}
