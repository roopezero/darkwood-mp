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
        // ---- what a coroutine was started with ------------------------------

        /// <summary>
        /// What an EventTrigger.fire or GameEvent.fire coroutine was started
        /// with.
        /// </summary>
        internal sealed class Started
        {
            public EventPart Part;
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
            if (!DoerRules.LeftToActor(FactsOf(type)))
            {
                return false;
            }

            if (t != null && DoerRules.CountsLeft(t.type == type, t.disabled))
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

        internal static bool SharedReplay(global::EventTrigger.Type type) =>
            DoerRules.SharedReplay(FactsOf(type));

        internal static bool OurPlayersAction() =>
            ByOurPlayer(global::EventTrigger.Type.onOpenDoor);

        /// <summary>
        /// Whether a trigger of this type, set off now, is our own player's
        /// doing.
        /// </summary>
        internal static bool ByOurPlayer(global::EventTrigger.Type type)
        {
            Doer doer = DoerRules.ByOurPlayer(FactsOf(type));
            if (doer == Doer.Stack)
            {
                return WorldActions.ByLocalPlayer()
                    || OnStack(typeof(DialogueWindow));
            }

            return doer == Doer.Yes;
        }

        /// <summary>
        /// What DoerRules needs to know as a trigger of this type is set off
        /// now.
        /// </summary>
        internal static DoerFacts FactsOf(global::EventTrigger.Type type)
        {
            var s = S;
            bool leaving = LeftToActor != null;
            return new DoerFacts
            {
                OwnAction = OwnAction,
                ActionType = ActionTypes.Contains(type),
                HitType = HitTypes.Contains(type),
                WorldType = WorldTypes.Contains(type),
                Area = type == global::EventTrigger.Type.area,
                Sight = type == global::EventTrigger.Type.onInSightOfPlayer
                    || type == global::EventTrigger.Type.onOutOfSightOfPlayer,
                Active = s != null && s.Active,
                Client = s != null && s.IsClient,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                Replaying = Coop.Guards.Replaying,
                Sharing = Coop.Guards.Sharing,
                Current = Current,
                PlayerEntered = PlayerEntered,
                TestAsPlayer = WorldActions.TestAsPlayer,
                Leaving = leaving,
                LeftType = leaving && LeftToActor.Contains(type),
            };
        }

        /// <summary>
        /// What FiringRules needs to know as a GameEvents starts to run, with
        /// the trigger firing now (Firing, or null).
        /// </summary>
        internal static FiringFacts FactsOf(Started firing, GameEvents ge)
        {
            if (firing == null)
            {
                return default;
            }

            var s = S;
            return new FiringFacts
            {
                Firing = true,
                HasTriggers = firing.Triggers != null,
                Index = firing.Index,
                World = firing.World,
                By = firing.By,
                Current = Current,
                Fired = ge.fired,
                MultipleFire = ge.multipleFire,
                Sharing = Coop.Guards.Sharing,
                Client = s != null && s.IsClient,
            };
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
            return WorldTriggerRules.HostsWorldTrigger(FactsOf(t, et, fired));
        }

        /// <summary>
        /// What WorldTriggerRules needs to know of a trigger of type fired,
        /// set off now. The scene, whether the object is a joiner's own copy
        /// and its name cost more than the rest: they are read only for a
        /// trigger the cheaper facts don't already rule out.
        /// </summary>
        internal static WorldTriggerFacts FactsOf(
            global::EventTrigger t, EventTriggers et,
            global::EventTrigger.Type fired)
        {
            var s = S;
            var f = new WorldTriggerFacts
            {
                Matches = t != null && et != null && t.type == fired,
                HostWorldType = HostWorldTypes.Contains(fired),
                Appears = fired == global::EventTrigger.Type.onSpawned
                    || fired == global::EventTrigger.Type.onGameObjectActivated,
                Active = s != null && s.Active,
                Client = s != null && s.IsClient,
                Current = Current,
                StepDepth = StepDepth,
            };

            if (!f.Matches || !f.HostWorldType || !f.Active
                || f.Current != EventPart.All || f.StepDepth > 0)
            {
                return f;
            }

            var r = ObjRef.Of(et.gameObject);
            return f with
            {
                InActiveScene = et.gameObject.scene
                    == UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene(),
                LocalOnly = f.Client
                    && Combat.NetEnemies.LocalOnly(et.gameObject),
                Named = r.Valid,
                Saved = r.K == ObjRef.Kind.Saved,
                MultipleFire = t.multipleFire,
            };
        }
    }
}
