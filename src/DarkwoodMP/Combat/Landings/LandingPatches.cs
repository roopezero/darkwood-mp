using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: the landing of one of our shared enemies' throws; what it
    /// leaves is sent after it (EnemyLanded).
    /// </summary>
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class ThrownItemOnCollideLanded
    {
        static void Prefix(ThrownItem __instance, out EnemyLandings __state)
        {
            __state = null;
            var coop = Coop.Current;
            if (coop != null && coop.Landings.LandingStarts(__instance))
            {
                __state = coop.Landings;
            }
        }

        static void Postfix(EnemyLandings __state)
        {
            if (__state != null)
            {
                __state.LandingEnds();
            }
        }

        static System.Exception Finalizer(
            EnemyLandings __state, System.Exception __exception)
        {
            if (__state != null && __exception != null)
            {
                __state.LandingFailed();
            }

            return __exception;
        }
    }

    /// <summary>
    /// Host: what the landing makes of its prefabToSpawnOnLand is noted, to
    /// send after it.
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class CoreAddPrefabLanded
    {
        static void Postfix(Object prefab, GameObject __result)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Landings.PrefabMade(prefab, __result);
            }
        }
    }

    /// <summary>
    /// Client: a copy of one of the host's enemies never throws anything (the
    /// host's throw lands here as EnemyLanded).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class CharacterSpawnProjectileCopy
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Character __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Landings.MayThrow(__instance);
        }
    }

    /// <summary>
    /// Host: one of our shared enemies throwing; its projectile is sent once
    /// it's aimed (EnemyProjectile).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class CharacterSpawnProjectile
    {
        static void Prefix(
            Character __instance, string _projectileType,
            out EnemyLandings __state)
        {
            __state = null;
            var coop = Coop.Current;
            if (coop != null
                && coop.Landings.ThrowStarts(__instance, _projectileType))
            {
                __state = coop.Landings;
            }
        }

        static void Postfix(EnemyLandings __state)
        {
            if (__state != null)
            {
                __state.ThrowEnds();
            }
        }

        static System.Exception Finalizer(
            EnemyLandings __state, System.Exception __exception)
        {
            if (__state != null && __exception != null)
            {
                __state.ThrowFailed();
            }

            return __exception;
        }
    }

    /// <summary>
    /// Host: the pooled projectile a noted throw makes is noted, to send
    /// after it.
    /// </summary>
    [HarmonyPatch]
    static class CoreAddPooledPrefabThrown
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(Core)).FindAll(
                m => m.Name == nameof(Core.AddPooledPrefab)
                    && m.ReturnType == typeof(Transform));

        static void Postfix(Transform __result)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Landings.PooledMade(__result);
            }
        }
    }

    /// <summary>
    /// Client: a copy of the host's enemy's projectile hits nothing and leaves
    /// nothing: it just goes.
    /// </summary>
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class ThrownItemOnCollideCopy
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ThrownItem __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return !coop.Landings.CopyEnds(__instance);
        }
    }

    /// <summary>
    /// Host: one of our shared enemies' ranged attack (Character.rangedAttack,
    /// which makes its sensor's prefab with Core.AddPrefab: the human spider's
    /// projectile); what it threw is sent once it's aimed (EnemySensorThrown).
    /// Where it lands and whom it hurts are the host's, as for
    /// spawnProjectile's.
    /// </summary>
    [HarmonyPatch(typeof(Character), "rangedAttack")]
    static class CharacterRangedAttack
    {
        static void Prefix(
            Character __instance, Character.SensorType sensorType,
            out EnemyLandings __state)
        {
            __state = null;
            var coop = Coop.Current;
            if (coop != null
                && coop.Landings.SensorThrowStarts(__instance, sensorType))
            {
                __state = coop.Landings;
            }
        }

        static void Postfix(EnemyLandings __state)
        {
            if (__state != null)
            {
                __state.SensorThrowEnds();
            }
        }

        static System.Exception Finalizer(
            EnemyLandings __state, System.Exception __exception)
        {
            if (__state != null && __exception != null)
            {
                __state.SensorThrowFailed();
            }

            return __exception;
        }
    }

    /// <summary>
    /// Host: the sensor's prefab a noted ranged attack makes is noted, to send
    /// after it.
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class CoreAddPrefabSensor
    {
        static void Postfix(GameObject __result)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Landings.SensorMade(__result);
            }
        }
    }
}
