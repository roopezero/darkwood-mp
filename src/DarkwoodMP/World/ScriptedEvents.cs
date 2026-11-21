using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
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
    /// (kind 5, Part.LaterPlayer), once each. If the host says no (it had
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
    /// trigger (Part.SharedAndMessages, NearMessages): a dialogue, an item
    /// given or a trip somewhere stays the host's player's, as it was in the
    /// host's single-player world, unless a joiner caused it (killed the NPC,
    /// broke the item, switched on the machine: Cause), whose game then runs
    /// its own and the shared part (kind 6, SendCaused). So an event set off by
    /// a death happens once, in the host's world, also when the one who died
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
    public static class ScriptedEvents
    {
        /// <summary>
        /// Which steps of a GameEvents run in this game right now.
        /// </summary>
        public enum Part : byte
        {
            /// <summary>All of them (nothing shared is going on).</summary>
            All = 0,
            /// <summary>
            /// A joiner's player set it off: its own part and the shared part.
            /// </summary>
            PlayerAndShared = 1,
            /// <summary>
            /// The host, for a joiner's event: the host's part and the shared
            /// part.
            /// </summary>
            HostAndShared = 2,
            /// <summary>Another player's event: only the shared part.</summary>
            SharedOnly = 3,
            /// <summary>
            /// The host's player came into (or went out of) a place the world
            /// already had running for another player (World.FarEvents): only
            /// its own part; the host's and the shared parts ran when the place
            /// was entered.
            /// </summary>
            PlayerOnly = 4,
            /// <summary>
            /// The host's world set it off by itself, and our player is near
            /// it: the shared part, and of the player's own part only the
            /// messages.
            /// </summary>
            SharedAndMessages = 5,
            /// <summary>
            /// Our player set off a once-only trigger that already fired for
            /// another player: only the steps a later player gets (LaterStep:
            /// its trip, its messages, ordinary items given); everything else
            /// ran once.
            /// </summary>
            LaterPlayer = 6,
        }

        /// <summary>
        /// How near the trigger a joiner's player must be to see the messages
        /// of a world-set-off event.
        /// </summary>
        public const float NearMessages = 2000f;

        /// <summary>
        /// The part being run now (set around a GameEvents.fire, and inside the
        /// coroutines it started).
        /// </summary>
        public static Part Current = Part.All;

        /// <summary>
        /// Host: the joiner whose event is being run now (the peer that sent
        /// the EventFired), or 0. Carried into the coroutines the event starts,
        /// as Current is (World.NpcFollowers uses it to know who recruited a
        /// follower).
        /// </summary>
        public static ulong By;

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

        private static bool IsPlayersObject(GameObject go, Player p) =>
            go == p.gameObject || go.transform.IsChildOf(p.transform);

        /// <summary>
        /// Whether this step runs in the part being run now. A moveInventory
        /// between world containers only is the host's, not the player's
        /// (World.Hideouts.IsWorldMove).
        /// </summary>
        public static bool Runs(GameEvent ge, GameObject thisGO)
        {
            if (Current == Part.LaterPlayer)
            {
                return LaterStep(ge);
            }

            if (Hideouts.IsWorldMove(ge))
            {
                return Runs(player: false, host: true, ge.type);
            }

            return Runs(
                IsPlayerStep(ge.type) || IsAreaLook(ge, thisGO)
                    || TargetsPlayer(ge),
                IsHostStep(ge.type), ge.type);
        }

        // ---- what only the player in an area sees and hears -----------------

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
        /// </summary>
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

        /// <summary>
        /// The GameEvents that a repeatable area trigger runs on enter or exit
        /// (built when needed).
        /// </summary>
        private static HashSet<GameEvents> _areaEvents;
        private static bool _areasDirty = true;

        /// <summary>
        /// Area triggers came or went (a scene loaded or unloaded, an
        /// EventTriggers woke up): look again.
        /// </summary>
        internal static void AreasChanged() => _areasDirty = true;

        /// <summary>
        /// Whether this step only changes how a place looks or sounds, in a
        /// repeatable area trigger's events.
        /// </summary>
        internal static bool IsAreaLook(GameEvent ge, GameObject thisGO)
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

                _areaEvents = set;
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

        private static bool Runs(bool player, bool host, GameEvent.Type t)
        {
            switch (Current)
            {
                case Part.SharedAndMessages:
                    return !host
                        && (!player || t == GameEvent.Type.displayMessage);
                case Part.PlayerAndShared:
                    return !host;
                case Part.HostAndShared:
                    return !player;
                case Part.SharedOnly:
                    return !player && !host;
                case Part.PlayerOnly:
                    return player && !host;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Run something with Current set to part (and Replaying, for a change
        /// that came from another game).
        /// </summary>
        public static void RunAs(Part part, bool replaying, Action a)
        {
            var was = Current;
            bool wasReplaying = WorldActions.Replaying;
            Current = part;
            if (replaying)
            {
                WorldActions.Replaying = true;
            }

            try
            {
                a();
            }
            finally
            {
                Current = was;
                WorldActions.Replaying = wasReplaying;
            }
        }

        private static Session S => Session.Instance;
        private static bool Debug => WorldActions.Debug;

        public static void Register(Session s)
        {
            s.Register(MsgType.EventFired, OnFired);
            s.OnSessionEnded += () =>
            {
                OwedParts.SessionEnded();
                Asking.Clear();
            };

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) =>
                AreasChanged();
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ =>
                AreasChanged();
        }

        // ---- what a coroutine was started with ------------------------------

        /// <summary>
        /// What an EventTrigger.fire or GameEvent.fire coroutine was started
        /// with.
        /// </summary>
        internal sealed class Started
        {
            public Part Part;
            /// <summary>The host's By when it was started.</summary>
            public ulong By;
            /// <summary>
            /// EventTrigger.fire only: our own player (or, on the host, the
            /// world) set it off, and it's to be shared when it fires.
            /// </summary>
            public bool ByPlayer;
            /// <summary>
            /// The host's world set it off by itself (sent as kind 3).
            /// </summary>
            public bool World;
            public EventTriggers Triggers;
            public int Index;
        }

        internal static readonly ConditionalWeakTable<object, Started>
            Coroutines = new ConditionalWeakTable<object, Started>();
        internal static void Remember(object coroutine, Started st)
        {
            Coroutines.Remove(coroutine);
            Coroutines.Add(coroutine, st);
        }

        /// <summary>
        /// The trigger whose coroutine is running now (EventTrigger.fire's
        /// body), if it's one to share.
        /// </summary>
        internal static Started Firing;

        /// <summary>
        /// True while EventTriggers.fireEventTrigger runs for something our own
        /// player did.
        /// </summary>
        internal static bool SettingOff;
        /// <summary>
        /// True while EventTriggers.OnTriggerEnter (or OnTriggerExit) runs for
        /// our own player's collider.
        /// </summary>
        internal static bool PlayerEntered;

        /// <summary>
        /// Trigger types the host's world sets off for everyone (see the class
        /// comment).
        /// </summary>
        private static readonly
            HashSet<global::EventTrigger.Type> HostWorldTypes =
            new HashSet<global::EventTrigger.Type>
        {
            global::EventTrigger.Type.onTime, global::EventTrigger.Type.onDeath,
            global::EventTrigger.Type.refreshWhenTurnedOn,
            global::EventTrigger.Type.onSpawned,
            global::EventTrigger.Type.onGameObjectActivated,
        };

        /// <summary>
        /// Trigger types the world sets off by itself, in every game (the
        /// clock, things appearing, chains).
        /// </summary>
        private static readonly HashSet<global::EventTrigger.Type> WorldTypes =
            new HashSet<global::EventTrigger.Type>(HostWorldTypes)
            {
                global::EventTrigger.Type.onFireGameEvents
            };

        // ---- triggers set off by a shared action ----------------------------
        // A door opened or closed, a thing switched on or off and a trap
        // disarmed are shared actions: the player who does it does it at once,
        // and the other games apply it as a replay. The triggers on the thing
        // (ActionTypes) are set off by the player who did it, in its game, and
        // sent from there as any trigger of theirs is (a joiner asks, the host
        // passes it on): so the actor runs its own part, the host its part and
        // the shared one, everyone else the shared one, once. A replay that set
        // them off again ran the whole event in every game (the host's player
        // got the joiner's steps), and the joiner's own request then came too
        // late ("already happened").

        /// <summary>
        /// Trigger types a player's shared action on a thing sets off (see
        /// above).
        /// </summary>
        internal static readonly
            HashSet<global::EventTrigger.Type> ActionTypes =
                new HashSet<global::EventTrigger.Type>
            {
                global::EventTrigger.Type.onOpenDoor,
                global::EventTrigger.Type.onCloseDoor,
                global::EventTrigger.Type.onOpenCloseDoor,
                global::EventTrigger.Type.onTurnOn,
                global::EventTrigger.Type.onTurnOff,
                global::EventTrigger.Type.onDisarmed,
            };

        /// <summary>
        /// Trigger types a player's hit on a character sets off
        /// (Character.getHit as byPlayer).
        /// </summary>
        internal static readonly HashSet<global::EventTrigger.Type> HitTypes =
            new HashSet<global::EventTrigger.Type>
        {
            global::EventTrigger.Type.onGetAttackedByPlayer,
        };

        /// <summary>
        /// While applying another player's action (or, on a joiner, the host's
        /// rollback of a thing): the triggers of these types it sets off aren't
        /// started here, as the player who did it sends them. Null otherwise.
        /// </summary>
        internal static HashSet<global::EventTrigger.Type> LeftToActor;
        /// <summary>
        /// How many triggers (of the type set off, and not switched off)
        /// LeftToActor kept from starting.
        /// </summary>
        internal static int LeftCount;
        /// <summary>
        /// While our player's own action that the host decided is done here as
        /// a replay (a disarm after the host's roll, a hit the host applied to
        /// its character): the triggers it sets off are our player's, as if it
        /// had done it here.
        /// </summary>
        internal static bool OwnAction;

        /// <summary>
        /// Do a, leaving the triggers of these types it sets off to the player
        /// who did it (LeftToActor). Returns how many were left.
        /// </summary>
        internal static int LeavingToActor(
            HashSet<global::EventTrigger.Type> types, Action a)
        {
            var was = LeftToActor;
            int count = LeftCount;
            LeftToActor = types;
            LeftCount = 0;
            try
            {
                a();
                return LeftCount;
            }
            finally
            {
                LeftToActor = was;
                LeftCount = count;
            }
        }

        /// <summary>
        /// The end of the host's result for a hit whose onGetAttackedByPlayer
        /// triggers it left to the hitter (Combat.NetEnemies.ApplyHit).
        /// </summary>
        internal const string Attacked = " attacked";

        /// <summary>
        /// Client: the host applied our player's hit on its character c. Our
        /// copy never takes the hit (the host's health is the one), so its
        /// triggers didn't go off here: if the host left them to us, set them
        /// off now, as our player's, so we run our part, the host its own and
        /// the shared one, and the others the shared one.
        /// </summary>
        internal static void HitApplied(Character c, string result)
        {
            if (c == null || result == null || !result.EndsWith(Attacked))
            {
                return;
            }

            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] our hit on {c.name}: its triggers are ours to "
                        + "set off");
            }

            AsOwnAction(
                () => Core.sendTriggerInfo(
                    c.gameObject,
                    global::EventTrigger.Type.onGetAttackedByPlayer));
        }

        /// <summary>
        /// Do a as our own player's action (OwnAction): the triggers it sets
        /// off are ours, and shared from here.
        /// </summary>
        internal static void AsOwnAction(Action a)
        {
            bool was = OwnAction;
            OwnAction = true;
            try
            {
                a();
            }
            finally
            {
                OwnAction = was;
            }
        }

        /// <summary>
        /// EventTrigger.fire: whether this trigger, set off now, is left to the
        /// player whose action it was (LeftToActor).
        /// </summary>
        internal static bool LeftToActorNow(
            global::EventTrigger t, global::EventTrigger.Type type)
        {
            if (LeftToActor == null || OwnAction || !LeftToActor.Contains(type))
            {
                return false;
            }

            if (t != null && t.type == type && !t.disabled)
            {
                LeftCount++;
                if (Debug)
                {
                    string triggersName = "?";
                    if (t.thisEventTriggers != null)
                    {
                        triggersName = t.thisEventTriggers.name;
                    }

                    Plugin.Log.LogInfo(
                        $"[world] {triggersName} ({type}): left to the player "
                            + "who did it");
                }
            }

            return true;
        }

        /// <summary>
        /// Client: a trigger set off by applying the host's action on a thing
        /// that no player did (the world's, an event's): only its shared part
        /// runs here; the host's game ran the rest.
        /// </summary>
        internal static bool SharedReplay(global::EventTrigger.Type type) =>
            WorldActions.Replaying && LeftToActor == null && !OwnAction
                && ActionTypes.Contains(type) && S != null && S.IsClient;

        /// <summary>
        /// Host: whether the shared action being sent now is our player's, so
        /// the triggers it set off here are sent as our player's (and the
        /// joiners leave them to that, LeftToActor).
        /// </summary>
        internal static bool OurPlayersAction() =>
            ByOurPlayer(global::EventTrigger.Type.onOpenDoor);

        /// <summary>
        /// Whether a trigger of this type, set off now, is our own player's
        /// doing.
        /// </summary>
        internal static bool ByOurPlayer(global::EventTrigger.Type type)
        {
            if (OwnAction
                && (ActionTypes.Contains(type) || HitTypes.Contains(type)))
            {
                return S != null && S.Active && WorldShare.InWorld
                    && !WorldShare.Loading;
            }

            if (WorldActions.Replaying || Current != Part.All
                || !WorldActions.Sharing || WorldTypes.Contains(type))
            {
                return false;
            }

            if (type == global::EventTrigger.Type.area)
            {
                return PlayerEntered || WorldActions.TestAsPlayer;
            }

            if (type == global::EventTrigger.Type.onInSightOfPlayer
                || type == global::EventTrigger.Type.onOutOfSightOfPlayer)
            {
                return true;
            }

            return WorldActions.ByLocalPlayer()
                || OnStack(typeof(DialogueWindow));
        }

        internal static bool OnStack(Type type)
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 24; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                Type t = null;
                if (frame != null)
                {
                    MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        t = method.DeclaringType;
                    }
                }

                for (; t != null; t = t.DeclaringType)
                {
                    if (t == type)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static IEnumerator Nothing()
        {
            yield break;
        }

        /// <summary>
        /// How many GameEvent step coroutines are running right now (a trigger
        /// set off inside one is a chain).
        /// </summary>
        internal static int StepDepth;

        /// <summary>
        /// Whether this trigger, set off now as fired, is one the host's world
        /// runs for everyone: the host sends it, a joiner leaves it to the
        /// host.
        /// </summary>
        internal static bool HostsWorldTrigger(
            global::EventTrigger t, EventTriggers et,
            global::EventTrigger.Type fired)
        {
            if (t == null || et == null || t.type != fired
                || !HostWorldTypes.Contains(fired))
            {
                return false;
            }

            var s = S;
            if (s == null || !s.Active || Current != Part.All || StepDepth > 0)
            {
                return false;
            }

            if (et.gameObject.scene
                != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            {
                return false;
            }

            if (s.IsClient && Combat.NetEnemies.LocalOnly(et.gameObject))
            {
                return false;
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return false;
            }

            if ((fired == global::EventTrigger.Type.onSpawned
                    || fired == global::EventTrigger.Type.onGameObjectActivated)
                && (r.K != ObjRef.Kind.Saved || t.multipleFire))
            {
                return false;
            }

            return true;
        }

        // ---- sending --------------------------------------------------------

        private enum Kind : byte
        {
            Trigger = 1,
            WorldEvent = 2,
            WorldTrigger = 3,
            TriggerExit = 4,
            PlayerPart = 5,
            WorldTriggerCaused = 6
        }

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
                Kind.WorldTrigger,
                Kind.WorldTriggerCaused
            })
            {
                var w = s.Begin(MsgType.EventFired);
                w.W.Write((byte)kind);
                r.Write(w);
                w.W.Write((byte)index);
                w.WriteStr(names);
                if (kind == Kind.WorldTrigger)
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
                OwedParts.Give(cause, t);
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
                Sync.PlayerSync instance = Sync.PlayerSync.Instance;
                if (instance != null)
                {
                    instance.FlushNow();
                }
            }
        }

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
        // is worked out from the loaded world and that record
        // (World.OwedParts), so it also holds for a mid-game joiner and after
        // the world is loaded again.

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
                && OwedParts.Owed(t);

        /// <summary>
        /// Our player had (or is having now) its own part of this trigger (it
        /// ran in our game).
        /// </summary>
        internal static void HadOurPart(global::EventTrigger t)
        {
            if (t != null && Once(t))
            {
                OwedParts.Had(t);
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
                || (S.IsHost && !OwedParts.HostOwe(t)))
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
            return s != null && s.Active && WorldActions.Sharing
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
            w.W.Write((byte)Kind.PlayerPart);
            r.Write(w);
            w.W.Write((byte)index);
            w.WriteStr("");
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

            if (OwedParts.Given(from, t))
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

            OwedParts.Give(from, t);
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

        /// <summary>
        /// Our player set off this trigger and its GameEvents are about to run:
        /// a joiner asks the host and runs its own and the shared part; the
        /// host tells the joiners to run the shared part.
        /// </summary>
        /// <returns>False if it couldn't be named (nothing was sent).</returns>
        internal static bool SendTrigger(
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

            if (WorldActions.OursAlone(et.gameObject))
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] event {et.name} #{index}: ours alone, run "
                            + "here");
                }

                return false;
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return false;
            }

            FlushPosition();
            var w = S.Begin(MsgType.EventFired);
            Kind kind;
            if (world)
            {
                kind = Kind.WorldTrigger;
            }
            else
            {
                kind = Kind.Trigger;
            }

            w.W.Write((byte)kind);
            r.Write(w);
            w.W.Write((byte)index);
            w.WriteStr(WorldEventsOf(TriggerAt(et, index)));
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
        internal static void SendExit(EventTriggers et, int index)
        {
            if (WorldActions.OursAlone(et.gameObject))
            {
                return; // nobody else has it
            }

            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid)
            {
                return;
            }

            FlushPosition();
            var w = S.Begin(MsgType.EventFired);
            w.W.Write((byte)Kind.TriggerExit);
            r.Write(w);
            w.W.Write((byte)index);
            w.WriteStr("");
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
            Coop.Current.Authority.Request(w, what, (ok, reason, _) =>
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
        private static void ExitArrived(
            EventTriggers et, global::EventTrigger t, int index, Part part,
            bool replaying)
        {
            if (t == null || t.disabled
                || t.type != global::EventTrigger.Type.area)
            {
                return;
            }

            if (t.firedExit)
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] exit of {et.name} #{index}: it already ran "
                            + "here");
                }

                return;
            }

            if (OurPlayerInside(et))
            {
                Plugin.Log.LogInfo(
                    $"[world] exit of {et.name} #{index}: our player is still "
                        + "in it, it runs when they leave");
                return;
            }

            t.firedExit = true;
            if (t.multipleFire)
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
                RunAs(part, replaying, ge.fire);
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
        internal static string WorldEventsOf(global::EventTrigger t)
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
        private static bool WorldEventsKnown(string list)
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
        private static void RunWorldEvents(
            string list, Part part, bool replaying, string what)
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
                    RunAs(part, replaying, () => events.fireWorldEvent(n));
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
        internal static bool AnimationSent;

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
        internal static bool FromAnimation(Component from, string name)
        {
            var s = S;
            if (s == null || !s.Active || !WorldShare.InWorld
                || Current != Part.All || WorldActions.Replaying)
            {
                return true;
            }

            if (from == null || from.GetComponent<Character>() == null
                || from.GetComponent<Player>() != null)
            {
                return true;
            }

            var events = Singleton<Events>.Instance;
            if (events == null || string.IsNullOrEmpty(name)
                || !events.worldEvents.TryGetValue(name, out var ge)
                || ge == null)
            {
                return true;
            }

            if (s.IsClient)
            {
                if (Combat.NetEnemies.LocalOnly(from.gameObject))
                {
                    return true;
                }

                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] left world event {name} from {from.name}'s "
                            + "animation to the host");
                }

                return false;
            }

            if (s.Peers.Count == 0 || (ge.fired && !ge.multipleFire))
            {
                return true;
            }

            var w = s.Begin(MsgType.EventFired);
            w.W.Write((byte)Kind.WorldEvent);
            default(ObjRef).Write(w);
            w.W.Write((byte)0);
            w.WriteStr(name);
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

        internal static void SendWorldEvent(string name, Action accepted)
        {
            var w = S.Begin(MsgType.EventFired);
            w.W.Write((byte)Kind.WorldEvent);
            default(ObjRef).Write(w);
            w.W.Write((byte)0);
            w.WriteStr(name);
            Send(w, $"world event {name}", accepted);
        }

        /// <summary>
        /// A Verdict's result: the requester is where the host doesn't run, so
        /// the event is all theirs.
        /// </summary>
        internal const string Alone = "alone";

        /// <summary>
        /// The host tells everyone; a joiner asks, and runs accepted (its own
        /// and the shared part) on a yes.
        /// </summary>
        private static void Send(PacketWriter w, string what, Action accepted)
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

            Coop.Current.Authority.Request(w, what, (ok, reason, result) =>
            {
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what} not run here: the host refused it "
                            + $"({reason})");
                    return;
                }

                if (result == Alone)
                {
                    // We were somewhere the host doesn't run
                    // (Authority.AcceptAway): every part of it is ours.
                    Plugin.Log.LogInfo(
                        $"[world] {what}: ours alone, the host says; running "
                            + "all of it here");
                    try
                    {
                        RunAs(Part.All, replaying: false, accepted);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't run {what}: {e.Message}");
                    }

                    return;
                }

                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what}: the host agreed, running our part "
                            + "and the shared part");
                }

                try
                {
                    RunAs(Part.PlayerAndShared, replaying: false, accepted);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Couldn't run {what}: {e.Message}");
                }
            }, inventory: false);
        }

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

            var kind = (Kind)r.R.ReadByte();
            var obj = ObjRef.Read(r);
            int index = r.R.ReadByte();
            string name = r.ReadStr();

            if (kind == Kind.WorldTrigger || kind == Kind.WorldTriggerCaused)
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

                bool ours = kind == Kind.WorldTriggerCaused;
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

            if (kind == Kind.PlayerPart)
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

            if (kind == Kind.TriggerExit)
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
            else if (kind == Kind.Trigger)
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

                    if (t != null && !TriggerAllowed(et, t))
                    {
                        return;
                    }

                    if (t != null)
                    {
                        // their own part runs in their game
                        OwedParts.Give(from, t);
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
                w.W.Write((byte)kind);
                obj.Write(w);
                w.W.Write((byte)index);
                w.WriteStr(name);
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
            ge != null && (!ge.fired || ge.multipleFire)
                && (ge.gameObject.activeInHierarchy || !ge.dontRunIfInactive);

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
            if (t.type != global::EventTrigger.Type.area)
            {
                Coop.Current.Authority.Refuse($"{et.name} isn't an area here");
                return false;
            }

            return Coop.Current.Authority.InReach(
                et.transform.position, 3000f, et.name);
        }

        /// <summary>
        /// The trigger can fire in the host's world and the requester is near
        /// it.
        /// </summary>
        private static bool TriggerAllowed(
            EventTriggers et, global::EventTrigger t)
        {
            if (t.disabled)
            {
                Coop.Current.Authority.Refuse("that event is switched off");
                return false;
            }

            if (t.fired && !t.multipleFire)
            {
                Coop.Current.Authority.Refuse("that has already happened");
                return false;
            }

            if (t.gameEvents == null)
            {
                Coop.Current.Authority.Refuse("nothing happens there");
                return false;
            }

            if (t.gameEvents.fired && !t.gameEvents.multipleFire)
            {
                Coop.Current.Authority.Refuse("its events have already run");
                return false;
            }

            if (!Coop.Current.Authority.InReach(
                et.transform.position, 3000f, et.name))
            {
                return false;
            }

            foreach (var req in t.eventRequirements)
            {
                if (req != null && HostChecks.Contains(req.type)
                    && !req.requirementsMet())
                {
                    Coop.Current.Authority.Refuse(
                        $"its requirement ({req.type}) isn't met here");
                    return false;
                }
            }

            return true;
        }
    }

    // ---- which part runs ----------------------------------------------------

    /// <summary>
    /// A step of a GameEvents that isn't in the part being run is skipped (its
    /// coroutine does nothing).
    /// </summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class GameEventPartPatch
    {
        static bool Prefix(
            GameEvent __instance, GameObject thisGO, ref IEnumerator __result)
        {
            // A new chapter and the game's end are the host's, whoever set them
            // off (see World.Chapters).
            bool runs;
            if (Chapters.IsStoryStep(__instance))
            {
                runs = Chapters.StoryStepRuns(__instance, thisGO);
            }
            else
            {
                runs = ScriptedEvents.Runs(__instance, thisGO);
            }

            if (runs)
            {
                return true;
            }

            __result = ScriptedEvents.Nothing();
            return false;
        }

        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            if (__result == null)
            {
                return;
            }

            if (ScriptedEvents.Current != ScriptedEvents.Part.All)
            {
                ScriptedEvents.Remember(__result, new ScriptedEvents.Started
                {
                    Part = ScriptedEvents.Current,
                    By = ScriptedEvents.By
                });
            }

            // The host sends what a world move left in each container (the
            // coroutine above keeps its part).
            if (Hideouts.Together && Session.Instance.IsHost
                && Hideouts.IsWorldMove(__instance))
            {
                __result = Hideouts.SendAfter(__result, __instance);
            }
        }
    }

    /// <summary>
    /// The coroutines a shared event started run in its part, also after a
    /// delay.
    /// </summary>
    [HarmonyPatch]
    static class CoroutinePartPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var owner in new[]
            {
                typeof(GameEvent),
                typeof(global::EventTrigger)
            })
            {
                foreach (var t in owner.GetNestedTypes(
                    BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (t.Name.StartsWith("<fire>")
                        && typeof(IEnumerator).IsAssignableFrom(t))
                    {
                        var m = AccessTools.Method(t, "MoveNext");
                        if (m != null)
                        {
                            yield return m;
                        }
                    }
                }
            }
        }

        static void Prefix(
            object __instance,
            out (ScriptedEvents.Part, ScriptedEvents.Started, bool, ulong)
                __state)
        {
            __state = (ScriptedEvents.Current, ScriptedEvents.Firing, false,
                ScriptedEvents.By);
            if (__instance.GetType().DeclaringType == typeof(GameEvent))
            {
                ScriptedEvents.StepDepth++;
            }

            if (!ScriptedEvents.Coroutines.TryGetValue(__instance, out var st))
            {
                return;
            }

            __state.Item3 = true;
            ScriptedEvents.Current = st.Part;
            if (st.ByPlayer)
            {
                ScriptedEvents.Firing = st;
            }
            else
            {
                ScriptedEvents.Firing = null;
            }

            ScriptedEvents.By = st.By;
        }

        static Exception Finalizer(
            object __instance, Exception __exception,
            (ScriptedEvents.Part, ScriptedEvents.Started, bool, ulong) __state)
        {
            if (__instance.GetType().DeclaringType == typeof(GameEvent)
                && ScriptedEvents.StepDepth > 0)
            {
                ScriptedEvents.StepDepth--;
            }

            if (__state.Item3)
            {
                ScriptedEvents.Current = __state.Item1;
                ScriptedEvents.Firing = __state.Item2;
                ScriptedEvents.By = __state.Item4;
            }

            return __exception;
        }
    }

    /// <summary>
    /// An EventTriggers woke up (a place built, an object spawned): the
    /// repeatable areas may have changed.
    /// </summary>
    [HarmonyPatch(typeof(EventTriggers), "Awake")]
    static class AreaTriggersChangedPatch
    {
        static void Postfix() => ScriptedEvents.AreasChanged();
    }

    // ---- who set it off -----------------------------------------------------

    [HarmonyPatch(typeof(EventTriggers), "OnTriggerEnter")]
    static class TriggerEnterPatch
    {
        static void Prefix(Collider other) =>
            ScriptedEvents.PlayerEntered = other != null
                && Player.Instance != null
                && other.gameObject == Player.Instance.gameObject;
        static Exception Finalizer(Exception __exception)
        {
            ScriptedEvents.PlayerEntered = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(EventTriggers), "OnTriggerExit")]
    static class TriggerExitPatch
    {
        static void Prefix(Collider other) =>
            ScriptedEvents.PlayerEntered = other != null
                && Player.Instance != null
                && other.gameObject == Player.Instance.gameObject;
        static Exception Finalizer(Exception __exception)
        {
            ScriptedEvents.PlayerEntered = false;
            return __exception;
        }
    }

    /// <summary>
    /// Our player left an area trigger: its exit events run here (a joiner's
    /// only its own and the shared part; the host's part is the host's) and are
    /// sent to the others (ScriptedEvents.SendExit).
    /// </summary>
    [HarmonyPatch(
        typeof(global::EventTrigger), nameof(global::EventTrigger.fireExit))]
    static class TriggerExitSharePatch
    {
        static void Prefix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType,
            out (bool, ScriptedEvents.Part) __state)
        {
            __state = (false, ScriptedEvents.Current);
            if (TriggerType != global::EventTrigger.Type.area
                || __instance.firedExit || __instance.disabled
                || __instance.type != TriggerType)
            {
                return;
            }

            if (__instance.thisEventTriggers == null
                || !ScriptedEvents.ByOurPlayer(TriggerType))
            {
                return;
            }

            __state.Item1 = true;
            if (Session.Instance.IsClient)
            {
                ScriptedEvents.Current = ScriptedEvents.Part.PlayerAndShared;
            }
        }

        static void Postfix(
            global::EventTrigger __instance,
            (bool, ScriptedEvents.Part) __state)
        {
            if (!__state.Item1)
            {
                return;
            }

            ScriptedEvents.Current = __state.Item2;
            if (!__instance.firedExit)
            {
                return; // it didn't run (its requirements)
            }

            var et = __instance.thisEventTriggers;
            try
            {
                ScriptedEvents.SendExit(
                    et, et.eventTriggers.IndexOf(__instance));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't send the exit of {et.name}: {e.Message}");
            }
        }

        static Exception Finalizer(
            Exception __exception, (bool, ScriptedEvents.Part) __state)
        {
            if (__state.Item1)
            {
                ScriptedEvents.Current = __state.Item2;
            }

            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(EventTriggers), nameof(EventTriggers.fireEventTrigger))]
    static class FireEventTriggerPatch
    {
        static void Prefix(
            global::EventTrigger.Type TriggerType, out bool __state)
        {
            __state = ScriptedEvents.SettingOff;
            ScriptedEvents.SettingOff = ScriptedEvents.ByOurPlayer(TriggerType);
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            ScriptedEvents.SettingOff = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Remember, for each trigger coroutine, whether our player set it off and
    /// in which part it runs.
    /// </summary>
    [HarmonyPatch(
        typeof(global::EventTrigger), nameof(global::EventTrigger.fire))]
    static class EventTriggerStartPatch
    {
        /// <summary>
        /// A joiner leaves the triggers the world sets off by itself to the
        /// host, which sends them.
        /// </summary>
        static bool Prefix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType, string Value,
            EventTriggers thisEventTriggers, ref IEnumerator __result,
            out bool __state)
        {
            __state = false;
            // Set off by applying another player's action: that player sets it
            // off in its game and sends it.
            if (ScriptedEvents.LeftToActorNow(__instance, TriggerType))
            {
                __state = true;
                __result = ScriptedEvents.Nothing();
                return false;
            }

            // A once-only trigger that fired for another player: our player's
            // own part of it (ScriptedEvents.LaterPart).
            if (ScriptedEvents.LaterFor(
                __instance, thisEventTriggers, TriggerType))
            {
                __state = true;
                __result = ScriptedEvents.LaterPart(
                    __instance, thisEventTriggers, TriggerType, Value);
                return false;
            }

            if (!Combat.NetEnemies.ClientMode
                || !ScriptedEvents.HostsWorldTrigger(
                    __instance, thisEventTriggers, TriggerType))
            {
                return true;
            }

            if (WorldActions.Debug
                && !(__instance.fired && !__instance.multipleFire)
                && !__instance.disabled)
            {
                int index = thisEventTriggers.eventTriggers.IndexOf(__instance);
                Plugin.Log.LogInfo(
                    $"[world] left {thisEventTriggers.name} "
                        + $"#{index} ({TriggerType}) to the host");
            }

            __state = true;
            __result = ScriptedEvents.Nothing();
            return false;
        }

        static void Postfix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType,
            EventTriggers thisEventTriggers, IEnumerator __result, bool __state)
        {
            if (__result == null || __state)
            {
                return;
            }

            var s = Session.Instance;
            if (s != null && s.IsHost && s.Peers.Count > 0
                && ScriptedEvents.HostsWorldTrigger(
                    __instance, thisEventTriggers, TriggerType))
            {
                ScriptedEvents.Remember(__result, new ScriptedEvents.Started
                {
                    Part = ScriptedEvents.Part.All,
                    ByPlayer = true,
                    World = true,
                    // a joiner behind it gets its player part (SendCaused)
                    By = ScriptedEvents.CauseFor(TriggerType),
                    Triggers = thisEventTriggers,
                    Index = thisEventTriggers.eventTriggers.IndexOf(__instance),
                });

                return;
            }

            // Seeing something is each player's own unless it happens once: a
            // trigger that fires each time it comes into (or goes out of) sight
            // would run for everyone whenever anyone looks.
            bool sight = __instance.type
                    == global::EventTrigger.Type.onInSightOfPlayer
                || __instance.type
                    == global::EventTrigger.Type.onOutOfSightOfPlayer;
            bool byPlayer = ScriptedEvents.SettingOff
                && thisEventTriggers != null
                && !(sight
                    && (__instance.multipleFire
                        || __instance.type
                            == global::EventTrigger.Type.onOutOfSightOfPlayer));
            // The host's action that no player did (the world's, an event's
            // step), applied here: the shared part.
            bool sharedReplay = !byPlayer
                && ScriptedEvents.Current == ScriptedEvents.Part.All
                && ScriptedEvents.SharedReplay(TriggerType);
            if (!byPlayer && !sharedReplay
                && ScriptedEvents.Current == ScriptedEvents.Part.All)
            {
                return;
            }

            ScriptedEvents.Part part;
            if (sharedReplay)
            {
                part = ScriptedEvents.Part.SharedOnly;
            }
            else
            {
                part = ScriptedEvents.Current;
            }

            int index = -1;
            if (byPlayer)
            {
                index = thisEventTriggers.eventTriggers.IndexOf(__instance);
            }

            ScriptedEvents.Remember(__result, new ScriptedEvents.Started
            {
                Part = part,
                By = ScriptedEvents.By,
                ByPlayer = byPlayer,
                Triggers = thisEventTriggers,
                Index = index,
            });
        }
    }

    /// <summary>
    /// The moment a trigger our player set off runs its GameEvents: share it. A
    /// joiner runs only its own part and the shared part here.
    /// </summary>
    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class GameEventsFirePatch
    {
        static bool Prefix(GameEvents __instance)
        {
            var f = ScriptedEvents.Firing;
            if (f == null || f.Index < 0 || f.Triggers == null
                || ScriptedEvents.Current != ScriptedEvents.Part.All)
            {
                return true;
            }

            ScriptedEvents.Firing = null; // once
            if (__instance.fired && !__instance.multipleFire)
            {
                return true;
            }

            if (!WorldActions.Sharing)
            {
                return true;
            }

            bool client = Session.Instance.IsClient;
            if (f.World && client)
            {
                // not ours to send (HostsWorldTrigger keeps these from starting
                // here)
                return true;
            }

            var ge = __instance;
            var et = f.Triggers;
            // A world trigger a joiner caused: the host's and the shared part
            // here, the joiner's own part in its game.
            if (f.World && f.By != 0
                && ScriptedEvents.SendCaused(et, f.Index, f.By))
            {
                ScriptedEvents.RunAs(
                    ScriptedEvents.Part.HostAndShared, replaying: false,
                    ge.fire);
                return false;
            }

            global::EventTrigger trigger = null;
            if (f.Index < et.eventTriggers.Count)
            {
                trigger = et.eventTriggers[f.Index];
            }

            bool repeatableArea = trigger != null
                && trigger.type == global::EventTrigger.Type.area
                && trigger.multipleFire;
            int exitedBefore = et.exited;
            bool sent = ScriptedEvents.SendTrigger(f.Triggers, f.Index, f.World,
                () =>
            {
                if (ge == null)
                {
                    return;
                }

                if (client)
                {
                    // ours ran here: not owed to us after this
                    ScriptedEvents.HadOurPart(trigger);
                }

                // Our player walked in and out again before the host's yes
                // came: its exit (the darkness and the sounds faded out) has
                // run here already, so the enter's look of the place mustn't
                // come after it.
                if (repeatableArea && et != null && et.exited > exitedBefore)
                {
                    ScriptedEvents.RunAs(
                        ScriptedEvents.Part.SharedOnly, replaying: false,
                        ge.fire);
                }
                else
                {
                    ge.fire();
                }
            });

            // A joiner runs its part once the host says yes
            // (ScriptedEvents.Send).
            return !(client && sent);
        }
    }

    /// <summary>
    /// A world event our player set off (a dialogue's outcome): share it.
    /// </summary>
    [HarmonyPatch(typeof(Events), nameof(Events.fireWorldEvent))]
    static class WorldEventPatch
    {
        static bool Prefix(Events __instance, string type)
        {
            if (ScriptedEvents.Current != ScriptedEvents.Part.All
                || WorldActions.Replaying || !WorldActions.Sharing
                || ScriptedEvents.AnimationSent)
            {
                return true;
            }

            if (type == EpilogueSync.PanEvent)
            {
                // each game runs its own, when the host says (EpilogueSync)
                return true;
            }

            if (type == null
                || !__instance.worldEvents.TryGetValue(type, out var ge)
                || ge == null || (ge.fired && !ge.multipleFire))
            {
                return true;
            }

            if (!WorldActions.ByLocalPlayer()
                && !ScriptedEvents.OnStack(typeof(DialogueWindow)))
            {
                return true;
            }

            // A dream NPC's outcome in a dream of our own: the host isn't
            // there, so it's ours alone, as in single player.
            if (WorldActions.InOwnArea())
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] world event {type}: ours alone, run here");
                }

                return true;
            }

            var events = __instance;
            ScriptedEvents.SendWorldEvent(type, () =>
            {
                if (events != null)
                {
                    events.fireWorldEvent(type);
                }
            });

            // A joiner runs it (its own and the shared part) once the host says
            // yes.
            return !Session.Instance.IsClient;
        }
    }

    /// <summary>
    /// Host: remember who switched an item on (ScriptedEvents.SwitchedOn), for
    /// its refreshWhenTurnedOn triggers.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class SwitcherPatch
    {
        static void Postfix(Item __instance) =>
            ScriptedEvents.SwitchedOn(__instance);
    }

    /// <summary>
    /// Host: an item that's on sets off its refreshWhenTurnedOn triggers from
    /// its ActionWhenTurnedOn's Update, long after it was switched on: whoever
    /// switched it on is their cause.
    /// </summary>
    [HarmonyPatch(
        typeof(ActionWhenTurnedOn), nameof(ActionWhenTurnedOn.Update))]
    static class SwitchedOnUpdatePatch
    {
        static void Prefix(ActionWhenTurnedOn __instance, out ulong __state)
        {
            __state = ScriptedEvents.Cause;
            if (__instance.turnedOn)
            {
                ScriptedEvents.Cause =
                    ScriptedEvents.SwitcherOf(__instance.GetComponent<Item>());
            }
        }

        static Exception Finalizer(Exception __exception, ulong __state)
        {
            ScriptedEvents.Cause = __state;
            return __exception;
        }
    }

    /// <summary>
    /// A world event a character's animation fires is the host's
    /// (ScriptedEvents.FromAnimation).
    /// </summary>
    [HarmonyPatch(
        typeof(AnimationTriggerListener),
        nameof(AnimationTriggerListener.checkFrameTrigger))]
    static class AnimationWorldEventPatch
    {
        static bool Prefix(
            AnimationTriggerListener __instance, string eventInfo,
            tk2dSpriteAnimationFrame.Trigger.Type type) =>
            type != tk2dSpriteAnimationFrame.Trigger.Type.fireWorldEvent
                || ScriptedEvents.FromAnimation(__instance, eventInfo);

        static Exception Finalizer(Exception __exception)
        {
            ScriptedEvents.AnimationSent = false;
            return __exception;
        }
    }
}
