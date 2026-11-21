using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioDogWatch
    {
        [HarmonyPatch(typeof(Character), nameof(Character.wakeup))]
        static class WakePatch
        {
            static void Prefix(Character __instance)
            {
                if (!Watched(__instance) || !__instance.sleeping
                    || __instance.wakingUp)
                {
                    return;
                }

                Watch.Woke();
                string name = __instance.name;
                string label = Id(__instance);
                string place = Rel(__instance.transform.position);
                string caller = Caller();
                string source = "-";
                if (Threat.NoiseSource != null)
                {
                    source = Threat.NoiseSource.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] WAKE {name}#{label} at {place} by {caller}; "
                        + $"noise source {source}; "
                        + $"{Dists(__instance.transform.position)}");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.beAlerted))]
        static class AlertPatch
        {
            static void Prefix(
                Character __instance, Transform destTarget,
                bool isAggressiveAction)
            {
                if (!Watched(__instance) || __instance.target == destTarget)
                {
                    return;
                }

                Watch.Alerted();
                string name = __instance.name;
                string label = Id(__instance);
                string place = Rel(__instance.transform.position);
                string asleep = "";
                if (__instance.sleeping)
                {
                    asleep = " asleep";
                }

                string eating = "";
                if (__instance.eating)
                {
                    eating = " eating";
                }

                string target = "-";
                if (destTarget != null)
                {
                    target = destTarget.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] ALERT {name}#{label} at "
                        + $"{place}{asleep}{eating} at {target} aggressive "
                        + $"{isAggressiveAction} by {Caller()}; "
                        + $"{Dists(__instance.transform.position)}");
            }
        }

        private static readonly Dictionary<int, float> _heardAt =
            new Dictionary<int, float>();

        [HarmonyPatch(typeof(Character), nameof(Character.heardSound), new[]
        {
            typeof(Vector3),
            typeof(float),
            typeof(bool),
            typeof(float),
            typeof(bool)
        })]
        static class HearPatch
        {
            static void Prefix(Character __instance, out float __state)
            {
                if (__instance != null)
                {
                    __state = __instance.hearingCounter;
                }
                else
                {
                    __state = 0f;
                }
            }

            static void Postfix(
                Character __instance, Vector3 Position, float soundRange,
                bool dangerousSound, float volume, float __state)
            {
                if (!Watched(__instance))
                {
                    return;
                }

                bool crossed = __instance.hearingCounter >= 1f && __state < 1f
                    || dangerousSound;
                int id = __instance.GetInstanceID();
                if (!crossed && _heardAt.TryGetValue(id, out var t)
                    && Time.unscaledTime - t < 2f)
                {
                    return;
                }

                _heardAt[id] = Time.unscaledTime;
                string name = __instance.name;
                string label = Id(__instance);
                string asleep = "";
                if (__instance.sleeping)
                {
                    asleep = " asleep";
                }

                string eating = "";
                if (__instance.eating)
                {
                    eating = " eating";
                }

                string loud = "";
                if (crossed)
                {
                    loud = "LOUD ";
                }

                float counter = __instance.hearingCounter;
                string origin = Rel(Position);
                float distance =
                    StateDump.Flat(Position - __instance.transform.position);
                string source = "-";
                if (Threat.NoiseSource != null)
                {
                    source = Threat.NoiseSource.name;
                }

                Plugin.Log.LogInfo(
                    $"[dogwatch] HEARD {name}#{label}{asleep}{eating} "
                        + $"{loud}counter {__state:0.00}->{counter:0.00} from "
                        + $"{origin} ({distance:0} away) range {soundRange:0} "
                        + $"vol {volume:0.00} danger {dangerousSound} source "
                        + $"{source} by {Caller()}");
            }
        }
    }
}
