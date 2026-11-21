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
    // ---- which part runs ----------------------------------------------------
    // These three keep EventParts (ModHost.Parts) also in a game played
    // alone: a coroutine can outlive its session, and StepDepth counts every
    // running step.

    /// <summary>
    /// A step of a GameEvents that isn't in the part being run is skipped (its
    /// coroutine does nothing).
    /// </summary>
    [HarmonyPatch(typeof(GameEvent), nameof(GameEvent.fire))]
    static class GameEventFire
    {
        static bool Prefix(
            GameEvent __instance, GameObject thisGO, ref IEnumerator __result)
        {
            var parts = Plugin.Instance.Host.Parts;
            if (parts.StepStarts(__instance, thisGO, out var instead))
            {
                return true;
            }

            __result = instead;
            return false;
        }

        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            __result =
                Plugin.Instance.Host.Parts.StepStarted(__instance, __result);
        }
    }

    /// <summary>
    /// The coroutines a shared event started run in its part, also after a
    /// delay.
    /// </summary>
    [HarmonyPatch]
    static class FireCoroutineMoveNext
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

        static void Prefix(object __instance, out EventParts.StepSaved __state)
        {
            __state = Plugin.Instance.Host.Parts.EnterStep(__instance);
        }

        static Exception Finalizer(
            object __instance, Exception __exception,
            EventParts.StepSaved __state)
        {
            Plugin.Instance.Host.Parts.LeaveStep(__instance, __state);
            return __exception;
        }
    }

    /// <summary>
    /// An EventTriggers woke up (a place built, an object spawned): the
    /// repeatable areas may have changed.
    /// </summary>
    [HarmonyPatch(typeof(EventTriggers), "Awake")]
    static class EventTriggersAwake
    {
        static void Postfix()
        {
            Plugin.Instance.Host.Parts.AreasChanged();
        }
    }

    // ---- who set it off -----------------------------------------------------

    [HarmonyPatch(typeof(EventTriggers), "OnTriggerEnter")]
    static class EventTriggersOnTriggerEnter
    {
        static void Prefix(Collider other)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.Entering(other);
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.Entered();
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(EventTriggers), "OnTriggerExit")]
    static class EventTriggersOnTriggerExit
    {
        static void Prefix(Collider other)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.Entering(other);
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.Entered();
            }

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
    static class EventTriggerFireExit
    {
        static void Prefix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType, out PartSaved __state)
        {
            __state = default;
            var coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Scripted.ExitStarts(__instance, TriggerType);
            }
        }

        static void Postfix(global::EventTrigger __instance, PartSaved __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.ExitRan(__instance, __state);
            }
        }

        static Exception Finalizer(Exception __exception, PartSaved __state)
        {
            Plugin.Instance.Host.Parts.PutBack(__state);
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(EventTriggers), nameof(EventTriggers.fireEventTrigger))]
    static class EventTriggersFireEventTrigger
    {
        static void Prefix(
            global::EventTrigger.Type TriggerType, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Scripted.SettingOffNow(TriggerType);
            }
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.SettingOff = __state;
            }

            return __exception;
        }
    }

    /// <summary>
    /// Remember, for each trigger coroutine, whether our player set it off and
    /// in which part it runs (ScriptedEvents.TriggerStarts and
    /// TriggerStarted).
    /// </summary>
    [HarmonyPatch(
        typeof(global::EventTrigger), nameof(global::EventTrigger.fire))]
    static class EventTriggerFire
    {
        static bool Prefix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType, string Value,
            EventTriggers thisEventTriggers, ref IEnumerator __result,
            out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            var f = new TriggerFire
            {
                Trigger = __instance,
                Triggers = thisEventTriggers,
                Type = TriggerType,
                Value = Value,
            };

            if (coop.Scripted.TriggerStarts(f, out var instead))
            {
                return true;
            }

            __state = true;
            __result = instead;
            return false;
        }

        static void Postfix(
            global::EventTrigger __instance,
            global::EventTrigger.Type TriggerType,
            EventTriggers thisEventTriggers, IEnumerator __result, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null || __result == null || __state)
            {
                return;
            }

            coop.Scripted.TriggerStarted(
                new TriggerFire
                {
                    Trigger = __instance,
                    Triggers = thisEventTriggers,
                    Type = TriggerType,
                },
                __result);
        }
    }

    /// <summary>
    /// The moment a trigger our player set off runs its GameEvents: share it
    /// (ScriptedEvents.EventsFire).
    /// </summary>
    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class GameEventsFire
    {
        static bool Prefix(GameEvents __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Scripted.EventsFire(__instance);
        }
    }

    /// <summary>
    /// A world event our player set off (a dialogue's outcome): share it.
    /// </summary>
    [HarmonyPatch(typeof(Events), nameof(Events.fireWorldEvent))]
    static class EventsFireWorldEvent
    {
        static bool Prefix(Events __instance, string type)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Scripted.OnWorldEvent(__instance, type);
        }
    }

    /// <summary>
    /// Host: remember who switched an item on (ScriptedEvents.SwitchedOn), for
    /// its refreshWhenTurnedOn triggers.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class ItemTurnOn
    {
        static void Postfix(Item __instance)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.SwitchedOn(__instance);
            }
        }
    }

    /// <summary>
    /// Host: an item that's on sets off its refreshWhenTurnedOn triggers from
    /// its ActionWhenTurnedOn's Update, long after it was switched on: whoever
    /// switched it on is their cause (ScriptedEvents.CauseOfUpdate).
    /// </summary>
    [HarmonyPatch(
        typeof(ActionWhenTurnedOn), nameof(ActionWhenTurnedOn.Update))]
    static class ActionWhenTurnedOnUpdate
    {
        static void Prefix(ActionWhenTurnedOn __instance, out ulong __state)
        {
            __state = 0;
            var coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Scripted.CauseOfUpdate(__instance);
            }
        }

        static Exception Finalizer(Exception __exception, ulong __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.Cause = __state;
            }

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
    static class AnimationTriggerListenerCheckFrameTrigger
    {
        static bool Prefix(
            AnimationTriggerListener __instance, string eventInfo,
            tk2dSpriteAnimationFrame.Trigger.Type type)
        {
            var coop = Coop.Current;
            if (coop == null
                || type != tk2dSpriteAnimationFrame.Trigger.Type.fireWorldEvent)
            {
                return true;
            }

            return coop.Scripted.FromAnimation(__instance, eventInfo);
        }

        static Exception Finalizer(Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Scripted.AnimationSent = false;
            }

            return __exception;
        }
    }
}
