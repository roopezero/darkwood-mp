using System.Collections.Generic;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class EnemyLandingPatch
    {
        static void Prefix(ThrownItem __instance, out bool __state)
        {
            __state = EnemyLandings.Landing == null
                && EnemyLandings.ByHostEnemy(
                    __instance, out EnemyLandings.LandingBy);
            if (__state)
            {
                EnemyLandings.Landing = __instance;
                EnemyLandings.Made.Clear();
            }
        }

        static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            EnemyLandings.Landing = null;
            EnemyLandings.Landed();
        }

        static System.Exception Finalizer(
            bool __state, System.Exception __exception)
        {
            if (__state && __exception != null)
            {
                EnemyLandings.Landing = null;
                EnemyLandings.Made.Clear();
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
    static class EnemyLandingSpawnPatch
    {
        static void Postfix(Object prefab, GameObject __result)
        {
            var t = EnemyLandings.Landing;
            bool noting = t != null && __result != null;
            bool land = noting && prefab != null
                && prefab == t.prefabToSpawnOnLand;
            if (LandingRules.TakesLanded(noting, land))
            {
                EnemyLandings.Made.Add(__result);
            }
        }
    }

    /// <summary>
    /// Client: a copy of one of the host's enemies never throws anything (the
    /// host's throw lands here as EnemyLanded).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class ClientNoEnemyThrowPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Character __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            bool copy = NetEnemies.Instance != null
                && NetEnemies.Instance.IsPuppet(__instance, out _);
            return LandingRules.MayThrow(
                NetEnemies.ClientMode, NetEnemies.Frozen.Contains(__instance),
                copy);
        }
    }

    /// <summary>
    /// Host: one of our shared enemies throwing; its projectile is sent once
    /// it's aimed (EnemyProjectile).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class EnemyThrowPatch
    {
        static void Prefix(
            Character __instance, string _projectileType, out bool __state)
        {
            var start = EnemyLandings.WatchOf(
                __instance, EnemyLandings.Throwing != null, out uint by);
            __state = LandingRules.Notes(start);
            if (__state)
            {
                EnemyLandings.ThrowingBy = by;
                EnemyLandings.Throwing = _projectileType;
                EnemyLandings.Thrown.Clear();
            }
        }

        static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            EnemyLandings.Threw();
            EnemyLandings.Throwing = null;
        }

        static System.Exception Finalizer(
            bool __state, System.Exception __exception)
        {
            if (__state && __exception != null)
            {
                EnemyLandings.Throwing = null;
                EnemyLandings.Thrown.Clear();
            }

            return __exception;
        }
    }

    [HarmonyPatch]
    static class EnemyThrowSpawnPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(Core)).FindAll(
                m => m.Name == nameof(Core.AddPooledPrefab)
                    && m.ReturnType == typeof(Transform));
        static void Postfix(Transform __result)
        {
            bool noting = EnemyLandings.Throwing != null && __result != null;
            if (LandingRules.TakesThrown(
                noting, noting && __result.GetComponent<ThrownItem>() != null,
                noting && EnemyLandings.Thrown.Contains(__result)))
            {
                EnemyLandings.Thrown.Add(__result);
            }
        }
    }

    /// <summary>
    /// Client: a copy of the host's enemy's projectile hits nothing and leaves
    /// nothing: it just goes.
    /// </summary>
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class EnemyProjectileCopyPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ThrownItem __instance)
        {
            if (__instance == null || !EnemyLandings.Copies.Remove(__instance))
            {
                return true;
            }

            EnemyLandings.Gone(__instance.transform);
            return false;
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
    static class EnemySensorThrowPatch
    {
        static void Prefix(
            Character __instance, Character.SensorType sensorType,
            out bool __state)
        {
            Character thrower = null;
            if (sensorType != null)
            {
                thrower = __instance;
            }

            var start = EnemyLandings.WatchOf(
                thrower, EnemyLandings.SensorThrowing != null, out uint by);
            __state = LandingRules.Notes(start);
            if (__state)
            {
                EnemyLandings.SensorThrowingBy = by;
                string sensorThrowing = sensorType.name;
                if (sensorThrowing == null)
                {
                    sensorThrowing = "";
                }

                EnemyLandings.SensorThrowing = sensorThrowing;
                EnemyLandings.SensorThrown.Clear();
            }
        }

        static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            EnemyLandings.SensorThrew();
            EnemyLandings.SensorThrowing = null;
        }

        static System.Exception Finalizer(
            bool __state, System.Exception __exception)
        {
            if (__state && __exception != null)
            {
                EnemyLandings.SensorThrowing = null;
                EnemyLandings.SensorThrown.Clear();
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class EnemySensorSpawnPatch
    {
        static void Postfix(GameObject __result)
        {
            if (EnemyLandings.SensorThrowing == null || __result == null)
            {
                return;
            }

            var ti = __result.GetComponent<ThrownItem>();
            bool thrown = ti != null;
            bool listed = thrown && EnemyLandings.SensorThrown.Contains(ti);
            if (LandingRules.TakesThrown(true, thrown, listed))
            {
                EnemyLandings.SensorThrown.Add(ti);
            }
        }
    }
}
