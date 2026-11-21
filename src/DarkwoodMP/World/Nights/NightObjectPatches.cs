using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Nights
{
    /// <summary>
    /// Host: the night event firing now, so the copies of its GameEvents are
    /// known.
    /// </summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class CustomEventFireNight
    {
        static void Prefix(CustomEvent __instance, out NightObjects __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Nights;
                __state.Fires(__instance);
            }
        }

        static void Finalizer(NightObjects __state)
        {
            if (__state != null)
            {
                __state.Fired();
            }
        }
    }

    /// <summary>
    /// The GameEvent step whose coroutine runs now (its spawns are made inside
    /// it).
    /// </summary>
    [HarmonyPatch]
    static class GameEventFireStep
    {
        private static readonly Dictionary<System.Type, FieldInfo> _this =
            new Dictionary<System.Type, FieldInfo>();

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in typeof(GameEvent).GetNestedTypes(
                BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (t.Name.StartsWith("<fire>")
                    && typeof(System.Collections.IEnumerator).IsAssignableFrom(
                        t))
                {
                    var m = AccessTools.Method(t, "MoveNext");
                    var f = AccessTools.Field(t, "<>4__this");
                    if (m != null && f != null)
                    {
                        _this[t] = f;
                        yield return m;
                    }
                }
            }
        }

        static void Prefix(object __instance, out NightObjects __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop == null
                || !_this.TryGetValue(__instance.GetType(), out var f))
            {
                return;
            }

            __state = coop.Nights;
            __state.StepRuns(f.GetValue(__instance) as GameEvent);
        }

        static System.Exception Finalizer(
            NightObjects __state, System.Exception __exception)
        {
            if (__state != null)
            {
                __state.StepDone();
            }

            return __exception;
        }
    }

    /// <summary>Host: what a night event's step made.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class CoreAddPrefabNight
    {
        static void Postfix(
            Object prefab, GameObject parentGO,
            GameObject __result)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Nights.Added(prefab, __result, parentGO);
            }
        }
    }
}
