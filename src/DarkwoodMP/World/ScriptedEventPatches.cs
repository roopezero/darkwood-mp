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
            FiringShare share =
                FiringRules.Share(ScriptedEvents.FactsOf(f, __instance));
            if (share == FiringShare.Pass)
            {
                return true;
            }

            ScriptedEvents.Firing = null; // once
            // FiringShare.Game: events that ran once already, no sharing, or
            // a client's world trigger: not ours to send (HostsWorldTrigger
            // keeps these from starting here).
            if (share == FiringShare.Game)
            {
                return true;
            }

            bool client = Session.Instance.IsClient;
            var ge = __instance;
            var et = f.Triggers;
            // A world trigger a joiner caused: the host's and the shared part
            // here, the joiner's own part in its game.
            if (share == FiringShare.Caused
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
                if (et != null
                    && FiringRules.LeftBeforeYes(
                        repeatableArea, exitedBefore, et.exited))
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
            return FiringRules.RunsAtOnce(client, sent);
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
                || Coop.Guards.Replaying || !Coop.Guards.Sharing
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
