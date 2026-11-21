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
        // ---- a once-only trigger, for the next player -----------------------
        //
        // A once-only trigger fires once in the shared world: its shared steps
        // (objects shown or hidden, flags, sounds) ran in every game, and only
        // the game of the player who set it off ran that player's own steps. If
        // those take the player somewhere (through a hole, down a ladder, into
        // a place) or hand them something, the next player who walks through it
        // found it marked fired and got nothing: stuck on the wrong side. So a
        // trigger marked fired by another game's player stays owed to ours
        // (IsOwed), and when our player sets it off, the steps a later player
        // gets (LaterStep) run for them alone (Part.LaterPlayer), after the
        // host's yes (kind 5) in a joiner's game. The host remembers who had
        // their part, by player key, so each player gets it once. What is owed
        // is worked out from the loaded world and that record (OwedParts), so
        // it also holds for a mid-game joiner and after the world is loaded
        // again.

        /// <summary>
        /// Joiner: owed triggers asked for and not answered yet.
        /// </summary>
        private static readonly HashSet<global::EventTrigger> Asking =
            new HashSet<global::EventTrigger>();

        /// <summary>
        /// The host's refusal of a later player's part they've had already (the
        /// joiner stops asking for it).
        /// </summary>
        private const string HadIt = "they have had their part of it";

        /// <summary>
        /// Whether our player is still owed its part of this fired once-only
        /// trigger.
        /// </summary>
        internal static bool IsOwed(global::EventTrigger t) =>
            t != null && Once(t) && OnceDone(t) && HasLaterSteps(t.gameEvents)
                && IsOwedHere(t);

        /// <summary>
        /// Our player had (or is having now) its own part of this trigger (it
        /// ran in our game).
        /// </summary>
        internal static void HadOurPart(global::EventTrigger t)
        {
            CoopSession coop = Coop.Current;
            if (coop != null && t != null && Once(t))
            {
                coop.Owed.Had(t);
            }
        }

        // OwedParts.Owed: never owed with no session.
        private static bool IsOwedHere(global::EventTrigger t)
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Owed.Owed(t);
        }

        // OwedParts.HostOwe: false with no session.
        private static bool HostOwe(global::EventTrigger t)
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Owed.HostOwe(t);
        }

        private static void GiveOwed(ulong peer, global::EventTrigger t)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Owed.Give(peer, t);
            }
        }

        /// <summary>
        /// Whether a later player gets this step of a once-only trigger that
        /// fired for someone else. Decided per step type, by what the step
        /// changes:
        /// - Trips (transportPlayerToObject without activeModifier,
        ///   transportToOutsideLocation, startDream, endDream, returnToWorld):
        ///   yes. They move only the player who runs them, and leaving one
        ///   player behind is the bug. A new chapter (transportPlayerToObject
        ///   with activeModifier) is not a trip: World.Chapters runs it once
        ///   for everyone.
        /// - What goes with a trip and only shows on that player's screen
        ///   (displayMessage, cameraEffect, timeScale, forbidInputs,
        ///   switchCantChangeForbidInputs, switchVisibleUI): yes, all of them,
        ///   so inputs taken away for a trip also come back.
        /// - The player's own records (addJournalItem, addAllJournalItems,
        ///   addRecipes, unlockAchievement): yes; each player has their own.
        /// - addOrRemoveInvItem: only an addition to the player's own inventory
        ///   (activeModifier) of ordinary items. Not an important item
        ///   (InvItem.isImportantItem: the story's quest items) or a key
        ///   (KeyReference): the story has one of those, the first player holds
        ///   it and can hand it over, and a second copy would let a quest step
        ///   be done twice or sold. Not a removal or a swap (activeModifier2,
        ///   relentlessPursuit): the story took its one item from the first
        ///   player, and taking it again would cost the next player something
        ///   for nothing. Not one into a world inventory (no activeModifier):
        ///   containers are shared, and it was added there once.
        /// - openDialogue: no. A conversation's outcome (flags, world events,
        ///   what the speaker gives or takes) is the shared story's, and it
        ///   happened once.
        /// - moveInventory: no; it moves things into a shared container.
        /// - endGame and every shared or host step: no; they ran once, in every
        ///   game.
        /// - Any other step that acts on the player (TargetsPlayer: a
        ///   runFunction on "player", such as diveIn): yes; it acted only on
        ///   the first player's own.
        /// </summary>
        internal static bool LaterStep(GameEvent e)
        {
            if (e == null || e.disabled)
            {
                return false;
            }

            switch (e.type)
            {
                case GameEvent.Type.transportPlayerToObject:
                    return !e.activeModifier;
                case GameEvent.Type.transportToOutsideLocation:
                case GameEvent.Type.startDream:
                case GameEvent.Type.endDream:
                case GameEvent.Type.returnToWorld:
                case GameEvent.Type.displayMessage:
                case GameEvent.Type.cameraEffect:
                case GameEvent.Type.timeScale:
                case GameEvent.Type.forbidInputs:
                case GameEvent.Type.switchCantChangeForbidInputs:
                case GameEvent.Type.switchVisibleUI:
                case GameEvent.Type.addJournalItem:
                case GameEvent.Type.addAllJournalItems:
                case GameEvent.Type.addRecipes:
                case GameEvent.Type.unlockAchievement:
                    return true;
                case GameEvent.Type.addOrRemoveInvItem:
                    if (!e.activeModifier || e.activeModifier2
                        || e.relentlessPursuit)
                    {
                        return false;
                    }

                    foreach (var o in e.prefabList)
                    {
                        GameObject gameObject = o as GameObject;
                        InvItem item = null;
                        if (gameObject != null)
                        {
                            item = gameObject.GetComponent<InvItem>();
                        }

                        if (item != null
                            && (item.isImportantItem
                                || item.GetComponent<KeyReference>() != null))
                        {
                            return false;
                        }
                    }

                    return true;
                case GameEvent.Type.openDialogue:
                case GameEvent.Type.moveInventory:
                case GameEvent.Type.endGame:
                    return false;
                default:
                    return TargetsPlayer(e);
            }
        }

        /// <summary>
        /// Whether a later player would get anything of these events.
        /// </summary>
        internal static bool HasLaterSteps(GameEvents ge) =>
            ge != null && ge.events.Exists(LaterStep);

        /// <summary>
        /// Whether this trigger runs its events only once: it fires once, or it
        /// fires each time but its GameEvents run once. The village cellar's
        /// first dive is the second kind: the passage's trigger fires on every
        /// use, but its dream (startDream_outside_village_ch2_cellar_01) runs
        /// once, so a second player diving never had it.
        /// </summary>
        private static bool Once(global::EventTrigger t) =>
            !t.multipleFire
                || (t.gameEvents != null && !t.gameEvents.multipleFire);

        /// <summary>
        /// Whether a once-only trigger (Once) has had its one run.
        /// </summary>
        private static bool OnceDone(global::EventTrigger t)
        {
            if (!t.multipleFire)
            {
                return t.fired;
            }

            return t.gameEvents != null && !t.gameEvents.multipleFire
                && t.gameEvents.fired;
        }

        /// <summary>
        /// Another player's once-only trigger fired here (only its shared part
        /// ran): our player is owed its own.
        /// </summary>
        private static void Owe(global::EventTrigger t, string what)
        {
            if (t == null || !Once(t) || !HasLaterSteps(t.gameEvents)
                || (S.IsHost && !HostOwe(t)))
            {
                return;
            }

            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {what} fired for another player: our player gets "
                        + "its own part when they set it off");
            }
        }

        /// <summary>
        /// Whether this owed trigger, set off now by our player, is to run as a
        /// later player's part (LaterPart).
        /// </summary>
        internal static bool LaterFor(
            global::EventTrigger t, EventTriggers et,
            global::EventTrigger.Type type)
        {
            if (t == null || et == null || t.type != type || !IsOwed(t))
            {
                return false;
            }

            var s = S;
            return s != null && s.Active && Coop.Guards.Sharing
                && Current == Part.All && SettingOff;
        }

        /// <summary>
        /// Our player set off an owed trigger: as EventTrigger.fire does (its
        /// delay, requirements, an area's warm-up, the item it reacts to), then
        /// the later player's steps, at once on the host, after the host's yes
        /// in a joiner's game.
        /// </summary>
        internal static IEnumerator LaterPart(
            global::EventTrigger t, EventTriggers et,
            global::EventTrigger.Type type, string value)
        {
            if (t.delay > 0f)
            {
                yield return new WaitForSeconds(t.delay);
            }

            if (t.disabled || !t.requirementsMet() || !IsOwed(t)
                || Asking.Contains(t))
            {
                yield break;
            }

            if (t.warmupTime != 0f && type == global::EventTrigger.Type.area)
            {
                yield return new WaitForSeconds(t.warmupTime);
                if (et == null || et.entered <= et.exited)
                {
                    yield break;
                }
            }

            if ((type == global::EventTrigger.Type.onTakeInvItem
                    || type == global::EventTrigger.Type.onPlaceItem)
                && !string.IsNullOrEmpty(t.reactsToItemType)
                && t.reactsToItemType != value)
            {
                yield break;
            }

            var ge = t.gameEvents;
            if (ge == null
                || !(ge.gameObject.activeInHierarchy || !ge.dontRunIfInactive))
            {
                yield break;
            }

            int index = et.eventTriggers.IndexOf(t);
            string what = $"our part of {et.name} #{index}";
            var s = S;
            if (s == null || !s.Active)
            {
                yield break;
            }

            if (s.IsHost)
            {
                RunLater(t, ge, what);
                yield break;
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                yield break;
            }

            FlushPosition();
            var w = s.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, new FiredEvent
            {
                Kind = EventKind.PlayerPart,
                Trigger = ObjRef.NameOf(r),
                Index = index,
                Name = "",
            });

            Asking.Add(t);
            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] asking for {what} {r}: it fired for another "
                        + "player");
            }

            Coop.Current.Authority.Request(w, what, (ok, reason, _) =>
            {
                Asking.Remove(t);
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what} not run here: the host refused it "
                            + $"({reason})");
                    // Had it already (before a reload, or in an earlier
                    // session), or the host won't give it: don't ask again each
                    // time. Too far away is asked again next time.
                    if (reason == null || !reason.Contains("(reach"))
                    {
                        HadOurPart(t);
                    }

                    return;
                }

                RunLater(t, ge, what);
            }, inventory: false);
        }

        /// <summary>
        /// Run the later player's steps of ge here, as GameEvents.fire starts
        /// them (it does nothing once fired).
        /// </summary>
        private static void RunLater(
            global::EventTrigger t, GameEvents ge, string what)
        {
            if (!IsOwed(t) || ge == null)
            {
                return;
            }

            HadOurPart(t);
            string steps = string.Join(
                ", ", ge.events.Where(LaterStep).Select(e => e.type));
            Plugin.Log.LogInfo(
                $"[world] {what}: it fired for another player, running only "
                    + "our player's steps "
                    + $"({steps})");
            RunAs(Part.LaterPlayer, replaying: false, () =>
            {
                foreach (var e in ge.events)
                {
                    if (e == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (ge.dontRunIfInactive)
                        {
                            ge.StartCoroutine(e.fire(ge.gameObject));
                        }
                        else
                        {
                            Singleton<Controller>.Instance.StartCoroutine(
                                e.fire(ge.gameObject));
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't run {what}: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// Host: a joiner asks for its own part of a once-only trigger that
        /// already fired (kind 5).
        /// </summary>
        private static void OnPlayerPart(
            ulong from, EventTriggers et, global::EventTrigger t)
        {
            if (t == null)
            {
                Coop.Current.Authority.Refuse("no such trigger here");
                return;
            }

            if (t.disabled)
            {
                Coop.Current.Authority.Refuse("that event is switched off");
                return;
            }

            if (!Once(t) || !OnceDone(t))
            {
                Coop.Current.Authority.Refuse("that hasn't happened here");
                return;
            }

            if (!HasLaterSteps(t.gameEvents))
            {
                Coop.Current.Authority.Refuse(
                    "nothing in it is a player's own");
                return;
            }

            if (Coop.Current.Owed.Given(from, t))
            {
                Coop.Current.Authority.Refuse(HadIt);
                return;
            }

            if (!Coop.Current.Authority.InReach(
                et.transform.position, 3000f, et.name))
            {
                return;
            }

            foreach (var req in t.eventRequirements)
            {
                if (req != null && HostChecks.Contains(req.type)
                    && !req.requirementsMet())
                {
                    Coop.Current.Authority.Refuse(
                        $"its requirement ({req.type}) isn't met here");
                    return;
                }
            }

            GiveOwed(from, t);
            ExpectTrips(from, t.gameEvents);
            Plugin.Log.LogInfo(
                $"[world] {S.NameOf(from)} set off {et.name} "
                    + $"#{et.eventTriggers.IndexOf(t)}, which already fired: "
                    + "their own part only");
        }

        /// <summary>
        /// Host: the joiner's game is about to run these events, which the host
        /// agreed to. A trip in them (transportPlayerToObject, not a chapter's)
        /// moves the joiner's player by itself, often by less than
        /// MoveCheck.Teleport (the village cellar's dive under water), which
        /// the movement check took for a speed hack and put them back. Tell it
        /// where each trip lands, so that jump is taken as the teleport it is.
        /// </summary>
        private static void ExpectTrips(ulong from, GameEvents ge)
        {
            var coop = Coop.Current;
            if (coop == null || ge == null)
            {
                return;
            }

            float delay = 0f;
            foreach (var e in ge.events)
            {
                if (e == null || e.disabled)
                {
                    continue;
                }

                delay += Mathf.Max(0f, e.delay);
                if (e.type != GameEvent.Type.transportPlayerToObject
                    || e.activeModifier)
                {
                    continue;
                }

                var to = FirstTarget(e);
                if (to == null)
                {
                    continue;
                }

                // The step fades the screen to black first (floatValue seconds,
                // 0.5 by default), then moves the player.
                coop.Moves.ExpectTrip(
                    from, to.transform.position,
                    10f + delay + Mathf.Max(e.floatValue, 0.5f));
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {S.NameOf(from)} is to be taken to {to.name} "
                            + $"at {to.transform.position}");
                }
            }
        }

        /// <summary>
        /// The object a step's first target resolves to (as GameEvent.fire puts
        /// its targets in order), or null.
        /// </summary>
        private static GameObject FirstTarget(GameEvent e)
        {
            if (e.targetGameObjects != null)
            {
                foreach (var go in e.targetGameObjects)
                {
                    if (go != null)
                    {
                        return go;
                    }
                }
            }

            UniqueObjects instance = Singleton<UniqueObjects>.Instance;
            Dictionary<string, UniqueObject> objects = null;
            if (instance != null)
            {
                objects = instance.objects;
            }

            if (e.targetUniqueObjects != null && objects != null)
            {
                foreach (var name in e.targetUniqueObjects)
                {
                    if (name != null && objects.TryGetValue(name, out var uo)
                        && uo != null)
                    {
                        return uo.gameObject;
                    }
                }
            }

            return null;
        }
    }
}
