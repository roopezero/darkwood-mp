using System;
using System.Collections;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Scripted
{
    internal sealed partial class ScriptedEvents
    {
        // ---- the game's calls, from ScriptedEventPatches --------------------

        /// <summary>
        /// EventTriggers.OnTriggerEnter or OnTriggerExit starts: whether it
        /// runs for our own player's collider (PlayerEntered).
        /// </summary>
        internal void Entering(Collider other)
        {
            PlayerEntered = other != null && Player.Instance != null
                && other.gameObject == Player.Instance.gameObject;
        }

        /// <summary>EventTriggers.OnTriggerEnter or OnTriggerExit ended.
        /// </summary>
        internal void Entered()
        {
            PlayerEntered = false;
        }

        /// <summary>
        /// EventTrigger.fireExit starts: our player left an area trigger, so
        /// a joiner runs only its own and the shared part of its exit
        /// events (the host's part is the host's). Returns the part to put
        /// back after the call (EventParts.PutBack), Set when it is ours to
        /// send.
        /// </summary>
        internal PartSaved ExitStarts(
            global::EventTrigger t, global::EventTrigger.Type type)
        {
            var saved = new PartSaved { Was = Parts.Current };
            if (type != global::EventTrigger.Type.area || t.firedExit
                || t.disabled || t.type != type)
            {
                return saved;
            }

            if (t.thisEventTriggers == null || !ByOurPlayer(type))
            {
                return saved;
            }

            saved.Set = true;
            if (Plugin.Instance.Host.Session.IsClient)
            {
                Parts.Current = EventPart.PlayerAndShared;
            }

            return saved;
        }

        /// <summary>
        /// EventTrigger.fireExit ran: send its exit events to the others
        /// (SendExit), if they ran and are ours to send.
        /// </summary>
        internal void ExitRan(global::EventTrigger t, PartSaved saved)
        {
            if (!saved.Set)
            {
                return;
            }

            Parts.PutBack(saved);
            if (!t.firedExit)
            {
                return; // it didn't run (its requirements)
            }

            var et = t.thisEventTriggers;
            try
            {
                SendExit(et, et.eventTriggers.IndexOf(t));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't send the exit of {et.name}: {e.Message}");
            }
        }

        /// <summary>
        /// EventTriggers.fireEventTrigger starts: whether our own player set
        /// it off (SettingOff). Returns SettingOff as it was, to put back.
        /// </summary>
        internal bool SettingOffNow(global::EventTrigger.Type type)
        {
            bool was = SettingOff;
            SettingOff = ByOurPlayer(type);
            return was;
        }

        /// <summary>
        /// EventTrigger.fire is called: whether the game's own coroutine
        /// starts. A joiner leaves the triggers the world sets off by itself
        /// to the host, which sends them; one that doesn't start gets the
        /// coroutine to run instead.
        /// </summary>
        internal bool TriggerStarts(TriggerFire f, out IEnumerator instead)
        {
            instead = Nothing();
            // Set off by applying another player's action: that player sets
            // it off in its game and sends it.
            if (LeftToActorNow(f.Trigger, f.Type))
            {
                return false;
            }

            // A once-only trigger that fired for another player: our
            // player's own part of it (LaterPart).
            if (LaterFor(f.Trigger, f.Triggers, f.Type))
            {
                instead = LaterPart(f.Trigger, f.Triggers, f.Type, f.Value);
                return false;
            }

            if (!Combat.NetEnemies.ClientMode
                || !HostsWorldTrigger(f.Trigger, f.Triggers, f.Type))
            {
                instead = null;
                return true;
            }

            var t = f.Trigger;
            if (Debug && !(t.fired && !t.multipleFire) && !t.disabled)
            {
                int index = f.Triggers.eventTriggers.IndexOf(t);
                Plugin.Log.LogInfo(
                    $"[world] left {f.Triggers.name} "
                        + $"#{index} ({f.Type}) to the host");
            }

            return false;
        }

        /// <summary>
        /// The game's EventTrigger.fire started its coroutine: remember
        /// whether our player set it off and in which part it runs.
        /// </summary>
        internal void TriggerStarted(TriggerFire f, IEnumerator coroutine)
        {
            var t = f.Trigger;
            var et = f.Triggers;
            var s = Plugin.Instance.Host.Session;
            bool hostWithPeers = s != null && s.IsHost && s.Peers.Count > 0;
            Remembered remembered = WorldTriggerRules.Remember(new RememberFacts
            {
                HostWithPeers = hostWithPeers,
                HostsWorld = hostWithPeers
                    && HostsWorldTrigger(t, et, f.Type),
                SettingOff = SettingOff,
                HasTriggers = et != null,
                Sight = t.type == global::EventTrigger.Type.onInSightOfPlayer
                    || t.type == global::EventTrigger.Type.onOutOfSightOfPlayer,
                OutOfSight = t.type
                    == global::EventTrigger.Type.onOutOfSightOfPlayer,
                MultipleFire = t.multipleFire,
                Current = Parts.Current,
                SharedReplay = SharedReplay(f.Type),
            });

            if (!remembered.Remembers)
            {
                return;
            }

            if (remembered.World)
            {
                Parts.Remember(coroutine, new Started
                {
                    Part = EventPart.All,
                    ByPlayer = true,
                    World = true,
                    // a joiner behind it gets its player part (SendCaused)
                    By = CauseFor(f.Type),
                    Triggers = et,
                    Index = et.eventTriggers.IndexOf(t),
                });

                return;
            }

            int index = -1;
            if (remembered.ByPlayer)
            {
                index = et.eventTriggers.IndexOf(t);
            }

            Parts.Remember(coroutine, new Started
            {
                Part = remembered.Part,
                By = Parts.By,
                ByPlayer = remembered.ByPlayer,
                Triggers = et,
                Index = index,
            });
        }

        /// <summary>
        /// GameEvents.fire: the moment a trigger our player set off runs its
        /// GameEvents, share it. A joiner runs only its own part and the
        /// shared part here. Returns whether the game's own call runs now.
        /// </summary>
        internal bool EventsFire(GameEvents ge)
        {
            var f = Parts.Firing;
            FiringShare share = FiringRules.Share(FactsOf(f, ge));
            if (share == FiringShare.Pass)
            {
                return true;
            }

            Parts.Firing = null; // once
            // FiringShare.Game: events that ran once already, no sharing, or
            // a client's world trigger: not ours to send (HostsWorldTrigger
            // keeps these from starting here).
            if (share == FiringShare.Game)
            {
                return true;
            }

            var et = f.Triggers;
            // A world trigger a joiner caused: the host's and the shared part
            // here, the joiner's own part in its game.
            if (share == FiringShare.Caused && SendCaused(et, f.Index, f.By))
            {
                Parts.RunAs(EventPart.HostAndShared, replaying: false, ge.fire);
                return false;
            }

            bool client = Plugin.Instance.Host.Session.IsClient;
            bool sent = SendTrigger(f.Triggers, f.Index, f.World,
                AfterYes(ge, et, f.Index, client));
            // A joiner runs its part once the host says yes (Send).
            return FiringRules.RunsAtOnce(client, sent);
        }

        /// <summary>
        /// What runs ge once the host says yes to our player's trigger
        /// (index in et).
        /// </summary>
        private Action AfterYes(
            GameEvents ge, EventTriggers et, int index, bool client)
        {
            global::EventTrigger trigger = null;
            if (index < et.eventTriggers.Count)
            {
                trigger = et.eventTriggers[index];
            }

            bool repeatableArea = trigger != null
                && trigger.type == global::EventTrigger.Type.area
                && trigger.multipleFire;
            int exitedBefore = et.exited;
            return () =>
            {
                if (ge == null)
                {
                    return;
                }

                if (client)
                {
                    // ours ran here: not owed to us after this
                    HadOurPart(trigger);
                }

                // Our player walked in and out again before the host's yes
                // came: its exit (the darkness and the sounds faded out) has
                // run here already, so the enter's look of the place mustn't
                // come after it.
                if (et != null
                    && FiringRules.LeftBeforeYes(
                        repeatableArea, exitedBefore, et.exited))
                {
                    Parts.RunAs(
                        EventPart.SharedOnly, replaying: false, ge.fire);
                }
                else
                {
                    ge.fire();
                }
            };
        }

        /// <summary>
        /// ActionWhenTurnedOn.Update starts: an item that's on sets off its
        /// refreshWhenTurnedOn triggers long after it was switched on, so
        /// whoever switched it on is their Cause. Returns Cause as it was,
        /// to put back.
        /// </summary>
        internal ulong CauseOfUpdate(ActionWhenTurnedOn action)
        {
            ulong was = Cause;
            if (action.turnedOn)
            {
                Cause = SwitcherOf(action.GetComponent<Item>());
            }

            return was;
        }
    }
}
