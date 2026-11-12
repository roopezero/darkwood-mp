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
    /// The game's scripted events (a GameEvents: a list of GameEvent steps) that a player sets off: walking into an
    /// area, using or opening something (EventTriggers), a dialogue's outcome (Events.fireWorldEvent), a padlock's
    /// events, a built option. Each step belongs to one of these parts:
    ///
    /// - the player's own: dialogue, dreams, being moved to a place, items given or taken, camera, messages,
    ///   journal, recipes, input and UI. They run only in the game of the player who set the event off.
    /// - the host's: doors and switching things on or off, which the host's patched calls then send to everyone,
    ///   and the clock. They run only on the host.
    /// - the story's turning points (a new chapter, the game's end): the host's whoever set them off, and never
    ///   run in a joiner's game (World.Chapters).
    /// - shared: everything else (objects shown, hidden, moved or removed, sprites, animations, sounds, flags,
    ///   characters spawned, chained events). It runs in every game. A joiner's own copies of enemies an event
    ///   spawns stay frozen and hidden as always (NetEnemies.LocalOnly); the host's are the ones that show.
    ///
    /// A joiner that sets one off asks the host first (EventFired as a Request) and runs nothing until the host
    /// says yes; then it runs its own part and the shared part. The host checks the trigger hasn't fired yet, the
    /// player is near it and the world-side requirements hold, marks it fired, runs the host's part and the shared
    /// part, and passes it on; the other games run the shared part and mark it fired, so it doesn't fire again for
    /// the next player. If the host says no (it had already happened there), nothing of it ran in the joiner's game,
    /// so there is nothing to undo. Waiting costs one round trip; running the shared part early and undoing it on a
    /// refusal can't be done reliably, since an event's steps show, hide, move and remove objects, set flags and chain
    /// to other events, and the game keeps no record of what they changed. When the host's own player sets one off,
    /// its game runs all of it and the joiners run the shared part.
    ///
    /// Triggers the world sets off by itself (HostWorldTypes: at a time of day, a saved object appearing or coming
    /// into view for the first time, something dying, an item turned on) are the host's: a joiner's game doesn't run
    /// its own copy of them, and the host sends each one as it runs it (EventFired kind 3). A joiner then runs the
    /// shared part, and of its player's own part only the messages, and only if its player is near the trigger
    /// (Part.SharedAndMessages, NearMessages): a dialogue, an item given or a trip somewhere stays the host's player's,
    /// as it was in the host's single-player world. So an event set off by a death happens once, in the host's world,
    /// also when the one who died was killed in a joiner's game (NPC deaths are shared, and the joiner's copy dies by
    /// the host's say). Not the host's: triggers in an area only the joiner is in (a dream, a separate location the
    /// host didn't place: NetEnemies.LocalOnly), appearing or coming into view for things made while playing or that
    /// fire each time (each game's own copy and its own culling), and triggers set off by another event's step
    /// (StepDepth: they chain on in the part their event runs in, in every game).
    ///
    /// The part is carried into everything the event starts later (its steps' coroutines, events it chains to),
    /// so a chained dialogue still opens only for the player who set it off.
    /// </summary>
    public static class ScriptedEvents
    {
        /// <summary>Which steps of a GameEvents run in this game right now.</summary>
        public enum Part : byte
        {
            /// <summary>All of them (nothing shared is going on).</summary>
            All = 0,
            /// <summary>A joiner's player set it off: its own part and the shared part.</summary>
            PlayerAndShared = 1,
            /// <summary>The host, for a joiner's event: the host's part and the shared part.</summary>
            HostAndShared = 2,
            /// <summary>Another player's event: only the shared part.</summary>
            SharedOnly = 3,
            /// <summary>
            /// The host's player came into (or went out of) a place the world already had running for another player
            /// (World.FarEvents): only its own part; the host's and the shared parts ran when the place was entered.
            /// </summary>
            PlayerOnly = 4,
            /// <summary>
            /// The host's world set it off by itself, and our player is near it: the shared part, and of the player's
            /// own part only the messages.
            /// </summary>
            SharedAndMessages = 5,
        }

        /// <summary>How near the trigger a joiner's player must be to see the messages of a world-set-off event.</summary>
        public const float NearMessages = 2000f;

        /// <summary>The part being run now (set around a GameEvents.fire, and inside the coroutines it started).</summary>
        public static Part Current = Part.All;

        /// <summary>
        /// Host: the joiner whose event is being run now (the peer that sent the EventFired), or 0. Carried into the
        /// coroutines the event starts, as Current is (World.NpcFollowers uses it to know who recruited a follower).
        /// </summary>
        public static ulong By;

        private static readonly HashSet<GameEvent.Type> PlayerSteps = new HashSet<GameEvent.Type>
        {
            GameEvent.Type.openDialogue, GameEvent.Type.startDream, GameEvent.Type.endDream, GameEvent.Type.returnToWorld,
            GameEvent.Type.transportToOutsideLocation, GameEvent.Type.transportPlayerToObject, GameEvent.Type.addOrRemoveInvItem,
            GameEvent.Type.moveInventory, GameEvent.Type.cameraEffect, GameEvent.Type.timeScale, GameEvent.Type.forbidInputs,
            GameEvent.Type.switchCantChangeForbidInputs, GameEvent.Type.switchVisibleUI, GameEvent.Type.displayMessage,
            GameEvent.Type.addJournalItem, GameEvent.Type.addAllJournalItems, GameEvent.Type.addRecipes,
            GameEvent.Type.unlockAchievement, GameEvent.Type.endGame, // endGame, a chapter and the epilogue are decided first by World.Chapters
        };
        private static readonly HashSet<GameEvent.Type> HostSteps = new HashSet<GameEvent.Type>
        {
            GameEvent.Type.modifyDoor, GameEvent.Type.closeDoor_NIEUZYWAJ, GameEvent.Type.unlockDoor_NIEUZYWAJ,
            GameEvent.Type.switchItemOnOff, GameEvent.Type.tweenTime,
        };

        /// <summary>Whether a step of this type is the player's own (runs only for whoever set the event off).</summary>
        public static bool IsPlayerStep(GameEvent.Type t) => PlayerSteps.Contains(t);

        /// <summary>Whether a step of this type is the host's (runs only in the host's game).</summary>
        public static bool IsHostStep(GameEvent.Type t) => HostSteps.Contains(t);

        /// <summary>Whether a step of this type runs in the part being run now.</summary>
        public static bool Runs(GameEvent.Type t) => Runs(PlayerSteps.Contains(t), HostSteps.Contains(t), t);

        /// <summary>
        /// Whether this step runs in the part being run now. A moveInventory between world containers only is the
        /// host's, not the player's (World.Hideouts.IsWorldMove).
        /// </summary>
        public static bool Runs(GameEvent ge) =>
            Hideouts.IsWorldMove(ge) ? Runs(player: false, host: true, ge.type) : Runs(ge.type);

        private static bool Runs(bool player, bool host, GameEvent.Type t)
        {
            switch (Current)
            {
                case Part.SharedAndMessages: return !host && (!player || t == GameEvent.Type.displayMessage);
                case Part.PlayerAndShared: return !host;
                case Part.HostAndShared: return !player;
                case Part.SharedOnly: return !player && !host;
                case Part.PlayerOnly: return player && !host;
                default: return true;
            }
        }

        /// <summary>Run something with Current set to part (and Replaying, for a change that came from another game).</summary>
        public static void RunAs(Part part, bool replaying, Action a)
        {
            var was = Current; bool wasReplaying = WorldActions.Replaying;
            Current = part;
            if (replaying) WorldActions.Replaying = true;
            try { a(); }
            finally { Current = was; WorldActions.Replaying = wasReplaying; }
        }

        private static Session S => Session.Instance;
        private static bool Debug => WorldActions.Debug;

        public static void Register(Session s) => s.Register(MsgType.EventFired, OnFired);

        // ---- what a coroutine was started with ----------------------------------------------------------

        /// <summary>What an EventTrigger.fire or GameEvent.fire coroutine was started with.</summary>
        internal sealed class Started
        {
            public Part Part;
            /// <summary>The host's By when it was started.</summary>
            public ulong By;
            /// <summary>EventTrigger.fire only: our own player (or, on the host, the world) set it off, and it's to be shared when it fires.</summary>
            public bool ByPlayer;
            /// <summary>The host's world set it off by itself (sent as kind 3).</summary>
            public bool World;
            public EventTriggers Triggers;
            public int Index;
        }
        internal static readonly ConditionalWeakTable<object, Started> Coroutines = new ConditionalWeakTable<object, Started>();
        internal static void Remember(object coroutine, Started st) { Coroutines.Remove(coroutine); Coroutines.Add(coroutine, st); }

        /// <summary>The trigger whose coroutine is running now (EventTrigger.fire's body), if it's one to share.</summary>
        internal static Started Firing;

        /// <summary>True while EventTriggers.fireEventTrigger runs for something our own player did.</summary>
        internal static bool SettingOff;
        /// <summary>True while EventTriggers.OnTriggerEnter runs for our own player's collider.</summary>
        internal static bool PlayerEntered;

        /// <summary>Trigger types the world sets off by itself, in every game (the clock, things appearing, chains).</summary>
        private static readonly HashSet<global::EventTrigger.Type> WorldTypes = new HashSet<global::EventTrigger.Type>
        {
            global::EventTrigger.Type.onTime, global::EventTrigger.Type.onSpawned, global::EventTrigger.Type.onGameObjectActivated,
            global::EventTrigger.Type.onFireGameEvents, global::EventTrigger.Type.refreshWhenTurnedOn, global::EventTrigger.Type.onDeath,
        };

        /// <summary>Whether a trigger of this type, set off now, is our own player's doing.</summary>
        internal static bool ByOurPlayer(global::EventTrigger.Type type)
        {
            if (WorldActions.Replaying || Current != Part.All || !WorldActions.Sharing || WorldTypes.Contains(type)) return false;
            if (type == global::EventTrigger.Type.area) return PlayerEntered || WorldActions.TestAsPlayer;
            if (type == global::EventTrigger.Type.onInSightOfPlayer || type == global::EventTrigger.Type.onOutOfSightOfPlayer) return true;
            return WorldActions.ByLocalPlayer() || OnStack(typeof(DialogueWindow));
        }

        internal static bool OnStack(Type type)
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 24; i++)
                for (var t = st.GetFrame(i)?.GetMethod()?.DeclaringType; t != null; t = t.DeclaringType)
                    if (t == type) return true;
            return false;
        }

        internal static IEnumerator Nothing() { yield break; }

        /// <summary>How many GameEvent step coroutines are running right now (a trigger set off inside one is a chain).</summary>
        internal static int StepDepth;

        /// <summary>Trigger types the host's world sets off for everyone (see the class comment).</summary>
        private static readonly HashSet<global::EventTrigger.Type> HostWorldTypes = new HashSet<global::EventTrigger.Type>
        {
            global::EventTrigger.Type.onTime, global::EventTrigger.Type.onDeath, global::EventTrigger.Type.refreshWhenTurnedOn,
            global::EventTrigger.Type.onSpawned, global::EventTrigger.Type.onGameObjectActivated,
        };

        /// <summary>
        /// Whether this trigger, set off now as fired, is one the host's world runs for everyone: the host sends it,
        /// a joiner leaves it to the host.
        /// </summary>
        internal static bool HostsWorldTrigger(global::EventTrigger t, EventTriggers et, global::EventTrigger.Type fired)
        {
            if (t == null || et == null || t.type != fired || !HostWorldTypes.Contains(fired)) return false;
            var s = S;
            if (s == null || !s.Active || Current != Part.All || StepDepth > 0) return false;
            if (et.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return false;
            if (s.IsClient && Combat.NetEnemies.LocalOnly(et.gameObject)) return false;
            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid) return false;
            if ((fired == global::EventTrigger.Type.onSpawned || fired == global::EventTrigger.Type.onGameObjectActivated)
                && (r.K != ObjRef.Kind.Saved || t.multipleFire)) return false;
            return true;
        }

        // ---- sending ------------------------------------------------------------------------------------

        private enum Kind : byte { Trigger = 1, WorldEvent = 2, WorldTrigger = 3 }

        /// <summary>
        /// Our player set off this trigger and its GameEvents are about to run: a joiner asks the host and runs its own
        /// and the shared part; the host tells the joiners to run the shared part.
        /// </summary>
        /// <returns>False if it couldn't be named (nothing was sent).</returns>
        internal static bool SendTrigger(EventTriggers et, int index, bool world, Action accepted)
        {
            var r = ObjRef.Of(et.gameObject);
            if (!r.Valid) return false;
            var w = S.Begin(MsgType.EventFired);
            w.W.Write((byte)(world ? Kind.WorldTrigger : Kind.Trigger));
            r.Write(w);
            w.W.Write((byte)index);
            w.WriteStr("");
            Send(w, $"{(world ? "world trigger" : "event")} {et.name} #{index} {r}", accepted);
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

        /// <summary>The host tells everyone; a joiner asks, and runs accepted (its own and the shared part) on a yes.</summary>
        private static void Send(PacketWriter w, string what, Action accepted)
        {
            if (Debug) Plugin.Log.LogInfo($"[world] sent {what}");
            if (S.IsHost) { S.Broadcast(w, Channel.Control, reliable: true); return; }
            Authority.Request(w, what, (ok, reason, _) =>
            {
                if (!ok) { Plugin.Log.LogInfo($"[world] {what} not run here: the host refused it ({reason})"); return; }
                if (Debug) Plugin.Log.LogInfo($"[world] {what}: the host agreed, running our part and the shared part");
                try { RunAs(Part.PlayerAndShared, replaying: false, accepted); }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't run {what}: {e.Message}"); }
            }, inventory: false);
        }

        // ---- receiving ----------------------------------------------------------------------------------

        private static void OnFired(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            var kind = (Kind)r.R.ReadByte();
            var obj = ObjRef.Read(r);
            int index = r.R.ReadByte();
            string name = r.ReadStr();

            if (kind == Kind.WorldTrigger)
            {
                // Only the host's world sends these; a joiner runs the shared part (and the messages, if near).
                if (s.IsHost) { Authority.Refuse("the world's own events are the host's"); return; }
                var et = ObjRef.Resolve<EventTriggers>(obj);
                var t = et != null && index < et.eventTriggers.Count ? et.eventTriggers[index] : null;
                if (Debug) Plugin.Log.LogInfo($"[world] got world trigger {obj} #{index}: " + (t != null ? $"{et.name} ({t.type})" : "not found here"));
                if (t == null || (t.fired && !t.multipleFire)) return;
                t.fired = true;
                t.firedExit = false;
                var ge = t.gameEvents;
                if (ge != null && (!ge.fired || ge.multipleFire) && (ge.gameObject.activeInHierarchy || !ge.dontRunIfInactive))
                {
                    var p = Player.Instance;
                    bool near = p != null && (p.transform.position - et.transform.position).sqrMagnitude < NearMessages * NearMessages;
                    try { RunAs(near ? Part.SharedAndMessages : Part.SharedOnly, replaying: true, ge.fire); }
                    catch (Exception e) { Plugin.Log.LogWarning($"Couldn't run world trigger {et.name} #{index}: {e.Message}"); }
                }
                return;
            }

            if (kind == Kind.Trigger)
            {
                var et = ObjRef.Resolve<EventTriggers>(obj);
                var t = et != null && index < et.eventTriggers.Count ? et.eventTriggers[index] : null;
                if (s.IsHost)
                {
                    if (t == null) { Authority.Refuse("no such event here"); return; }
                    if (!TriggerAllowed(et, t)) return;
                }
                if (Debug) Plugin.Log.LogInfo($"[world] got event {obj} #{index}: " + (t != null ? et.name : "not found here"));
                if (t == null) return;
                t.fired = true;
                t.firedExit = false;
                var ge = t.gameEvents;
                if (ge != null && (!ge.fired || ge.multipleFire) && (ge.gameObject.activeInHierarchy || !ge.dontRunIfInactive))
                {
                    var wasBy = By; if (s.IsHost) By = from;
                    try { RunAs(s.IsHost ? Part.HostAndShared : Part.SharedOnly, replaying: s.IsClient, ge.fire); }
                    catch (Exception e) { Plugin.Log.LogWarning($"Couldn't run event {et.name} #{index}: {e.Message}"); }
                    finally { By = wasBy; }
                }
            }
            else
            {
                var events = Singleton<Events>.Instance;
                GameEvents ge = null;
                bool known = events != null && events.worldEvents.TryGetValue(name, out ge) && ge != null;
                if (s.IsHost)
                {
                    if (!known) { Authority.Refuse($"no world event {name} here"); return; }
                    if (ge.fired && !ge.multipleFire) { Authority.Refuse($"{name} has already happened"); return; }
                }
                if (Debug) Plugin.Log.LogInfo($"[world] got world event {name}" + (known ? "" : ": not found here"));
                if (!known) return;
                var wasBy = By; if (s.IsHost) By = from;
                try { RunAs(s.IsHost ? Part.HostAndShared : Part.SharedOnly, replaying: s.IsClient, () => events.fireWorldEvent(name)); }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't run world event {name}: {e.Message}"); }
                finally { By = wasBy; }
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

        /// <summary>
        /// Requirements the host can check against its own world. The others are about the player who set it off
        /// (what they carry, their state), or are flags and fired events that reach the host a moment later.
        /// </summary>
        private static readonly HashSet<EventTriggerRequirement.Type> HostChecks = new HashSet<EventTriggerRequirement.Type>
        {
            EventTriggerRequirement.Type.itemState, EventTriggerRequirement.Type.itemTurnedOn, EventTriggerRequirement.Type.npcStateIsDead,
        };

        /// <summary>The trigger can fire in the host's world and the requester is near it.</summary>
        private static bool TriggerAllowed(EventTriggers et, global::EventTrigger t)
        {
            if (t.disabled) { Authority.Refuse("that event is switched off"); return false; }
            if (t.fired && !t.multipleFire) { Authority.Refuse("that has already happened"); return false; }
            if (t.gameEvents == null) { Authority.Refuse("nothing happens there"); return false; }
            if (t.gameEvents.fired && !t.gameEvents.multipleFire) { Authority.Refuse("its events have already run"); return false; }
            if (!Authority.InReach(et.transform.position, 3000f, et.name)) return false;
            foreach (var req in t.eventRequirements)
                if (req != null && HostChecks.Contains(req.type) && !req.requirementsMet()) { Authority.Refuse($"its requirement ({req.type}) isn't met here"); return false; }
            return true;
        }
    }

    // ---- which part runs ------------------------------------------------------------------------------

    /// <summary>A step of a GameEvents that isn't in the part being run is skipped (its coroutine does nothing).</summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class GameEventPartPatch
    {
        static bool Prefix(GameEvent __instance, GameObject thisGO, ref IEnumerator __result)
        {
            // A new chapter and the game's end are the host's, whoever set them off (see World.Chapters).
            if (Chapters.IsStoryStep(__instance) ? Chapters.StoryStepRuns(__instance, thisGO) : ScriptedEvents.Runs(__instance)) return true;
            __result = ScriptedEvents.Nothing();
            return false;
        }

        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            if (__result == null) return;
            if (ScriptedEvents.Current != ScriptedEvents.Part.All)
                ScriptedEvents.Remember(__result, new ScriptedEvents.Started { Part = ScriptedEvents.Current, By = ScriptedEvents.By });
            // The host sends what a world move left in each container (the coroutine above keeps its part).
            if (Hideouts.Together && Session.Instance.IsHost && Hideouts.IsWorldMove(__instance))
                __result = Hideouts.SendAfter(__result, __instance);
        }
    }

    /// <summary>The coroutines a shared event started run in its part, also after a delay.</summary>
    [HarmonyPatch]
    static class CoroutinePartPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var owner in new[] { typeof(GameEvent), typeof(global::EventTrigger) })
                foreach (var t in owner.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                    if (t.Name.StartsWith("<fire>") && typeof(IEnumerator).IsAssignableFrom(t))
                    {
                        var m = AccessTools.Method(t, "MoveNext");
                        if (m != null) yield return m;
                    }
        }

        static void Prefix(object __instance, out (ScriptedEvents.Part, ScriptedEvents.Started, bool, ulong) __state)
        {
            __state = (ScriptedEvents.Current, ScriptedEvents.Firing, false, ScriptedEvents.By);
            if (__instance.GetType().DeclaringType == typeof(GameEvent)) ScriptedEvents.StepDepth++;
            if (!ScriptedEvents.Coroutines.TryGetValue(__instance, out var st)) return;
            __state.Item3 = true;
            ScriptedEvents.Current = st.Part;
            ScriptedEvents.Firing = st.ByPlayer ? st : null;
            ScriptedEvents.By = st.By;
        }

        static Exception Finalizer(object __instance, Exception __exception, (ScriptedEvents.Part, ScriptedEvents.Started, bool, ulong) __state)
        {
            if (__instance.GetType().DeclaringType == typeof(GameEvent) && ScriptedEvents.StepDepth > 0) ScriptedEvents.StepDepth--;
            if (__state.Item3) { ScriptedEvents.Current = __state.Item1; ScriptedEvents.Firing = __state.Item2; ScriptedEvents.By = __state.Item4; }
            return __exception;
        }
    }

    // ---- who set it off -------------------------------------------------------------------------------

    [HarmonyPatch(typeof(EventTriggers), "OnTriggerEnter")]
    static class TriggerEnterPatch
    {
        static void Prefix(Collider other) =>
            ScriptedEvents.PlayerEntered = other != null && Player.Instance != null && other.gameObject == Player.Instance.gameObject;
        static Exception Finalizer(Exception __exception) { ScriptedEvents.PlayerEntered = false; return __exception; }
    }

    [HarmonyPatch(typeof(EventTriggers), nameof(EventTriggers.fireEventTrigger))]
    static class FireEventTriggerPatch
    {
        static void Prefix(global::EventTrigger.Type TriggerType, out bool __state)
        {
            __state = ScriptedEvents.SettingOff;
            ScriptedEvents.SettingOff = ScriptedEvents.ByOurPlayer(TriggerType);
        }
        static Exception Finalizer(Exception __exception, bool __state) { ScriptedEvents.SettingOff = __state; return __exception; }
    }

    /// <summary>Remember, for each trigger coroutine, whether our player set it off and in which part it runs.</summary>
    [HarmonyPatch(typeof(global::EventTrigger), nameof(global::EventTrigger.fire))]
    static class EventTriggerStartPatch
    {
        /// <summary>A joiner leaves the triggers the world sets off by itself to the host, which sends them.</summary>
        static bool Prefix(global::EventTrigger __instance, global::EventTrigger.Type TriggerType, EventTriggers thisEventTriggers, ref IEnumerator __result, out bool __state)
        {
            __state = false;
            if (!Combat.NetEnemies.ClientMode || !ScriptedEvents.HostsWorldTrigger(__instance, thisEventTriggers, TriggerType)) return true;
            if (WorldActions.Debug && !(__instance.fired && !__instance.multipleFire) && !__instance.disabled)
                Plugin.Log.LogInfo($"[world] left {thisEventTriggers.name} #{thisEventTriggers.eventTriggers.IndexOf(__instance)} ({TriggerType}) to the host");
            __state = true;
            __result = ScriptedEvents.Nothing();
            return false;
        }

        static void Postfix(global::EventTrigger __instance, global::EventTrigger.Type TriggerType, EventTriggers thisEventTriggers, IEnumerator __result, bool __state)
        {
            if (__result == null || __state) return;
            var s = Session.Instance;
            if (s != null && s.IsHost && s.Peers.Count > 0 && ScriptedEvents.HostsWorldTrigger(__instance, thisEventTriggers, TriggerType))
            {
                ScriptedEvents.Remember(__result, new ScriptedEvents.Started
                {
                    Part = ScriptedEvents.Part.All, ByPlayer = true, World = true,
                    Triggers = thisEventTriggers, Index = thisEventTriggers.eventTriggers.IndexOf(__instance),
                });
                return;
            }
            // Seeing something is each player's own unless it happens once: a trigger that fires each time it comes into
            // (or goes out of) sight would run for everyone whenever anyone looks.
            bool sight = __instance.type == global::EventTrigger.Type.onInSightOfPlayer || __instance.type == global::EventTrigger.Type.onOutOfSightOfPlayer;
            bool byPlayer = ScriptedEvents.SettingOff && thisEventTriggers != null
                && !(sight && (__instance.multipleFire || __instance.type == global::EventTrigger.Type.onOutOfSightOfPlayer));
            if (!byPlayer && ScriptedEvents.Current == ScriptedEvents.Part.All) return;
            ScriptedEvents.Remember(__result, new ScriptedEvents.Started
            {
                Part = ScriptedEvents.Current,
                By = ScriptedEvents.By,
                ByPlayer = byPlayer,
                Triggers = thisEventTriggers,
                Index = byPlayer ? thisEventTriggers.eventTriggers.IndexOf(__instance) : -1,
            });
        }
    }

    /// <summary>
    /// The moment a trigger our player set off runs its GameEvents: share it. A joiner runs only its own part and
    /// the shared part here.
    /// </summary>
    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class GameEventsFirePatch
    {
        static bool Prefix(GameEvents __instance)
        {
            var f = ScriptedEvents.Firing;
            if (f == null || f.Index < 0 || f.Triggers == null || ScriptedEvents.Current != ScriptedEvents.Part.All) return true;
            ScriptedEvents.Firing = null; // once
            if (__instance.fired && !__instance.multipleFire) return true;
            if (!WorldActions.Sharing) return true;
            bool client = Session.Instance.IsClient;
            if (f.World && client) return true; // not ours to send (HostsWorldTrigger keeps these from starting here)
            var ge = __instance;
            bool sent = ScriptedEvents.SendTrigger(f.Triggers, f.Index, f.World, () => { if (ge != null) ge.fire(); });
            // A joiner runs its part once the host says yes (ScriptedEvents.Send).
            return !(client && sent);
        }
    }

    /// <summary>A world event our player set off (a dialogue's outcome): share it.</summary>
    [HarmonyPatch(typeof(Events), nameof(Events.fireWorldEvent))]
    static class WorldEventPatch
    {
        static bool Prefix(Events __instance, string type)
        {
            if (ScriptedEvents.Current != ScriptedEvents.Part.All || WorldActions.Replaying || !WorldActions.Sharing) return true;
            if (type == null || !__instance.worldEvents.TryGetValue(type, out var ge) || ge == null || (ge.fired && !ge.multipleFire)) return true;
            if (!WorldActions.ByLocalPlayer() && !ScriptedEvents.OnStack(typeof(DialogueWindow))) return true;
            var events = __instance;
            ScriptedEvents.SendWorldEvent(type, () => { if (events != null) events.fireWorldEvent(type); });
            // A joiner runs it (its own and the shared part) once the host says yes.
            return !Session.Instance.IsClient;
        }
    }
}
