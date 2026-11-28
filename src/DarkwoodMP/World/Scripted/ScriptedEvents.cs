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
    /// <summary>
    /// The game's scripted events (a GameEvents: a list of GameEvent steps)
    /// that a player sets off: walking into an area, using or opening something
    /// (EventTriggers), a dialogue's outcome (Events.fireWorldEvent), a
    /// padlock's events, a built option. Each step belongs to one of these
    /// parts:
    ///
    /// - the player's own: dialogue, dreams, being moved to a place, items
    ///   given or taken, camera, messages, journal, recipes, input and UI. They
    ///   run only in the game of the player who set the event off.
    /// - the host's: doors and switching things on or off, which the host's
    ///   patched calls then send to everyone, and the clock. They run only on
    ///   the host.
    /// - the story's turning points (a new chapter, the game's end): the host's
    ///   whoever set them off, and never run in a joiner's game
    ///   (World.Chapters).
    /// - shared: everything else (objects shown, hidden, moved or removed,
    ///   sprites, animations, sounds, flags, characters spawned, chained
    ///   events). It runs in every game. Except in an area trigger that fires
    ///   each time a player walks in: there the steps that only change how the
    ///   place looks and sounds (colour fades, particles, sounds) are the
    ///   player's own (IsAreaLook). A joiner's own copies of enemies an event
    ///   spawns stay frozen and hidden as always (NetEnemies.LocalOnly); the
    ///   host's are the ones that show.
    ///
    /// A joiner that sets one off asks the host first (EventFired as a Request)
    /// and runs nothing until the host says yes; then it runs its own part and
    /// the shared part. The host checks the trigger hasn't fired yet, the
    /// player is near it and the world-side requirements hold, marks it fired,
    /// runs the host's part and the shared part, and passes it on; the other
    /// games run the shared part and mark it fired, so its shared part doesn't
    /// run again for the next player. Its player steps that a later player gets
    /// (a trip somewhere, its messages, ordinary items: LaterStep) are still
    /// owed to each other player, and run for them alone when they set it off
    /// (kind 5, EventPart.LaterPlayer), once each. If the host says no (it had
    /// already happened there), nothing of it ran in the joiner's game, so
    /// there is nothing to undo. Waiting costs one round trip; running the
    /// shared part early and undoing it on a refusal can't be done reliably,
    /// since an event's steps show, hide, move and remove objects, set flags
    /// and chain to other events, and the game keeps no record of what they
    /// changed. When the host's own player sets one off, its game runs all of
    /// it and the joiners run the shared part.
    ///
    /// Triggers the world sets off by itself (HostWorldTypes: at a time of day,
    /// a saved object appearing or coming into view for the first time,
    /// something dying, an item turned on) are the host's: a joiner's game
    /// doesn't run its own copy of them, and the host sends each one as it runs
    /// it (EventFired kind 3). A joiner then runs the shared part, and of its
    /// player's own part only the messages, and only if its player is near the
    /// trigger (EventPart.SharedAndMessages, FiredEventRules.NearMessages): a
    /// dialogue, an item given or a trip somewhere stays the host's player's,
    /// as it was in the host's single-player world, unless a joiner caused it
    /// (killed the NPC, broke the item, switched on the machine: Cause), whose
    /// game then runs its own and the shared part (kind 6, SendCaused). So an
    /// event set off by a death happens once, in the host's world, also when
    /// the one who died
    /// was killed in a joiner's game (NPC deaths are shared, and the joiner's
    /// copy dies by the host's say). Not the host's: triggers in an area only
    /// the joiner is in (a dream, a separate location the host didn't place:
    /// NetEnemies.LocalOnly), appearing or coming into view for things made
    /// while playing or that fire each time (each game's own copy and its own
    /// culling), and triggers set off by another event's step (StepDepth: they
    /// chain on in the part their event runs in, in every game).
    ///
    /// Leaving an area trigger runs its exit events
    /// (EventTrigger.gameEventsExit: the stop of a sound its enter started,
    /// objects shown again) in the leaver's game at once, in its own and the
    /// shared part, and sends them (EventFired kind 4, SendExit); every other
    /// game runs the shared part (the host also its own part) unless its own
    /// player is still in that area, in which case their own exit runs it
    /// later. The enter's shared part ran everywhere, so an exit left in one
    /// game kept the place's looping whispers playing for good in the others.
    ///
    /// The part is carried into everything the event starts later (its steps'
    /// coroutines, events it chains to), so a chained dialogue still opens only
    /// for the player who set it off.
    /// </summary>
    internal sealed partial class ScriptedEvents
    {
        private static readonly HashSet<GameEvent.Type> PlayerSteps =
            new HashSet<GameEvent.Type>
        {
            GameEvent.Type.openDialogue, GameEvent.Type.startDream,
            GameEvent.Type.endDream, GameEvent.Type.returnToWorld,
            GameEvent.Type.transportToOutsideLocation,
            GameEvent.Type.transportPlayerToObject,
            GameEvent.Type.addOrRemoveInvItem,
            GameEvent.Type.moveInventory, GameEvent.Type.cameraEffect,
            GameEvent.Type.timeScale, GameEvent.Type.forbidInputs,
            GameEvent.Type.switchCantChangeForbidInputs,
            GameEvent.Type.switchVisibleUI, GameEvent.Type.displayMessage,
            GameEvent.Type.addJournalItem, GameEvent.Type.addAllJournalItems,
            GameEvent.Type.addRecipes,
            // endGame, a chapter and the epilogue are decided first by
            // World.Chapters
            GameEvent.Type.unlockAchievement, GameEvent.Type.endGame,
        };

        private static readonly HashSet<GameEvent.Type> HostSteps =
            new HashSet<GameEvent.Type>
        {
            GameEvent.Type.modifyDoor, GameEvent.Type.closeDoor_NIEUZYWAJ,
            GameEvent.Type.unlockDoor_NIEUZYWAJ,
            GameEvent.Type.switchItemOnOff, GameEvent.Type.tweenTime,
        };

        /// <summary>
        /// Whether a step of this type is the player's own (runs only for
        /// whoever set the event off).
        /// </summary>
        public static bool IsPlayerStep(GameEvent.Type t) =>
            PlayerSteps.Contains(t);

        /// <summary>
        /// Whether a step of this type is the host's (runs only in the host's
        /// game).
        /// </summary>
        public static bool IsHostStep(GameEvent.Type t) =>
            HostSteps.Contains(t);

        /// <summary>
        /// Whether a step acts on the player of the game that runs it: the
        /// unique object "player" (or another unique name that stands for the
        /// player's object) or the player's own object, or one inside it, among
        /// its targets. Each game resolves these to its own Player.Instance, so
        /// run as a shared step every player did it: the cellar's dive holes
        /// call runFunction diveIn on "player", which stops the player and
        /// takes its inputs away, and one player diving made everyone dive.
        /// Such a step is the player's own, whatever its type.
        /// A modifyMainScript step other than eventToFireAfterLoad doesn't use
        /// its targets (GameEvent.fire skips them). An eventToFireAfterLoad
        /// step puts GameEvents on the list this game runs after its next load:
        /// the cellar's "addDivingOutEventAfterLoad" lists "setPlayerDivingOut"
        /// (diveOut on "player") for the diver coming back from the dive's
        /// dream. It's the player's own when what it lists acts on the player.
        /// </summary>
        public static bool TargetsPlayer(GameEvent ge) =>
            TargetsPlayer(ge, nested: true);

        /// <summary>
        /// Player functions the story calls on "player" that change the world
        /// around them, not the player: a wolf put in the player's hideout, the
        /// wolfman taken from it, Maciek put beside them, the world events
        /// refreshed, the night shadows started or ended. They stay shared, as
        /// before.
        /// </summary>
        private static readonly HashSet<string> WorldFunctions =
            new HashSet<string>
        {
            "spawnWolfInCurrentHideout", "wolf_stealSister",
            "special_teleportMaciek", "special_refreshWorldEvents",
            "tryToSpawnShadow", "endShadows", "shadowLightsTurnOff",
        };

        private static bool TargetsPlayer(GameEvent ge, bool nested)
        {
            if (ge == null)
            {
                return false;
            }

            if (ge.type == GameEvent.Type.runFunction && ge.Value != null
                && WorldFunctions.Contains(ge.Value))
            {
                return false;
            }

            if (ge.type == GameEvent.Type.modifyMainScript)
            {
                if (ge.mainScriptModify
                        != GameEvent.MainScriptModify.eventToFireAfterLoad
                    || !nested || ge.targetGameObjects == null)
                {
                    return false;
                }

                foreach (var go in ge.targetGameObjects)
                {
                    if (go != null)
                    {
                        foreach (var listed in go.GetComponents<GameEvents>())
                        {
                            if (listed != null
                                && listed.events.Exists(
                                    e => TargetsPlayer(e, nested: false)))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            var p = Player.Instance;
            if (ge.targetUniqueObjects != null
                && ge.targetUniqueObjects.Count > 0)
            {
                UniqueObjects instance = Singleton<UniqueObjects>.Instance;
                Dictionary<string, UniqueObject> objects = null;
                if (instance != null)
                {
                    objects = instance.objects;
                }

                foreach (var name in ge.targetUniqueObjects)
                {
                    if (name == "player")
                    {
                        return true;
                    }

                    if (p != null && name != null && objects != null
                        && objects.TryGetValue(name, out var uo) && uo != null
                        && IsPlayersObject(uo.gameObject, p))
                    {
                        return true;
                    }
                }
            }

            if (p != null && ge.targetGameObjects != null)
            {
                foreach (var go in ge.targetGameObjects)
                {
                    if (go != null && IsPlayersObject(go, p))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a step plays a sound with no target: GameEvent.fire plays
        /// a sound step (or the old playSound) with no targets at the
        /// listener (AudioController.Play without a parent, or
        /// RandomWorldSounds.playGlobalSound), as loud wherever the player
        /// is. The cellar's dives play water_teleport_01B so. Stopping a
        /// sound isn't one: it stops what plays in that game, if anything.
        /// </summary>
        internal static bool IsListenerSound(GameEvent ge)
        {
            bool play = ge.type == GameEvent.Type.playSound_NIEUZYWAJ
                || (ge.type == GameEvent.Type.sound
                    && ge.soundModifyType == GameEvent.SoundModify.play);
            if (!play || ge.getTargetGameObjectsFromParent)
            {
                return false;
            }

            if (ge.targetGameObjects != null
                && ge.targetGameObjects.Exists(go => go != null))
            {
                return false;
            }

            if (ge.targetUniqueObjects != null
                && ge.targetUniqueObjects.Count > 0)
            {
                return false;
            }

            return ge.locationObjects == null || ge.locationObjects.Count == 0;
        }

        private static bool IsPlayersObject(GameObject go, Player p) =>
            go == p.gameObject || go.transform.IsChildOf(p.transform);

        internal static StepType StepTypeOf(GameEvent.Type t)
        {
            switch (t)
            {
                case GameEvent.Type.transportPlayerToObject:
                    return StepType.PlayerTrip;
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
                    return StepType.Later;
                case GameEvent.Type.addOrRemoveInvItem:
                    return StepType.Item;
                case GameEvent.Type.openDialogue:
                case GameEvent.Type.moveInventory:
                case GameEvent.Type.endGame:
                    return StepType.NotLater;
                default:
                    return StepType.Other;
            }
        }

        /// <summary>
        /// One of the items an addOrRemoveInvItem step adds is an important
        /// item or a key.
        /// </summary>
        internal static bool AddsSpecialItem(GameEvent e)
        {
            if (e.type != GameEvent.Type.addOrRemoveInvItem)
            {
                return false;
            }

            if (e.prefabList == null)
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
                    return true;
                }
            }

            return false;
        }

        private readonly CoopSession _coop;

        internal ScriptedEvents(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.EventFired, OnFired);
        }

        private static Session S => Plugin.Instance.Host.Session;

        private static bool Debug => WorldActions.Debug;

        /// <summary>The part being run now (ModHost.Parts).</summary>
        private static EventParts Parts => Plugin.Instance.Host.Parts;
    }
}
