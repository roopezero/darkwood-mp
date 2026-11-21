using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The player dropped what they held at their feet (ThrowSync.AfterDrop).
    /// </summary>
    [HarmonyPatch(typeof(Player), "dropItem")]
    static class PlayerDropItem
    {
        static void Prefix(
            Player __instance, out (GameObject go, string type) __state)
        {
            __state = ThrowSync.Before(__instance);
        }

        static void Postfix(
            Player __instance, (GameObject go, string type) __state)
        {
            ThrowSync.AfterDrop(__instance, __state);
        }
    }

    /// <summary>
    /// The player threw what they held (ThrowSync.AfterThrow).
    /// </summary>
    [HarmonyPatch(typeof(Player), "throwItem")]
    static class PlayerThrowItem
    {
        static void Prefix(
            Player __instance, out (GameObject go, string type) __state)
        {
            __state = ThrowSync.Before(__instance);
        }

        static void Postfix(
            Player __instance, (GameObject go, string type) __state)
        {
            ThrowSync.AfterThrow(__instance, __state);
        }
    }

    /// <summary>
    /// A thrown thing's impact (ThrownHits): a client sends no hit on the
    /// host's enemies from it, the host's copy of the throw hits them there.
    /// </summary>
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class ThrownItemOnCollide
    {
        static void Prefix() => ThrownHits.Enter();

        static void Postfix() => ThrownHits.Leave();

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrownHits.Failed(__exception);
            return __exception;
        }
    }

    /// <summary>
    /// An explosion (ThrownHits), as for ThrownItemOnCollide.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "explode")]
    static class ExplodesExplode
    {
        static void Prefix() => ThrownHits.Enter();

        static void Postfix() => ThrownHits.Leave();

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrownHits.Failed(__exception);
            return __exception;
        }
    }

    /// <summary>
    /// What an explosion spawns (ThrownHits), as for ThrownItemOnCollide.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "spawnObjects")]
    static class ExplodesSpawnObjects
    {
        static void Prefix() => ThrownHits.Enter();

        static void Postfix() => ThrownHits.Leave();

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrownHits.Failed(__exception);
            return __exception;
        }
    }

    /// <summary>
    /// An explosive set off (ThrownHits), as for ThrownItemOnCollide.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), nameof(Explodes.onActivate),
        new System.Type[0])]
    static class ExplodesOnActivate
    {
        static void Prefix() => ThrownHits.Enter();

        static void Postfix() => ThrownHits.Leave();

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrownHits.Failed(__exception);
            return __exception;
        }
    }

    /// <summary>
    /// A flame touching something (ThrowSync.FlameTouching).
    /// </summary>
    [HarmonyPatch(typeof(Flame), "onCollideWith")]
    static class FlameOnCollideWith
    {
        static void Prefix(
            Flame __instance, Transform _transform, Collider _collider,
            out GameObject __state)
        {
            __state = ThrowSync.FlameTouching(
                __instance, _transform, _collider);
        }

        static void Postfix(GameObject __state)
        {
            ThrowSync.FlameTouched(__state);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            ThrowSync.FlameDone();
            return __exception;
        }
    }

    /// <summary>
    /// What a landing or exploding thrown thing spawns from a prefab
    /// (ThrownHits.Spawn).
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab),
        new[]
        {
            typeof(Object),
            typeof(Vector3),
            typeof(Quaternion),
            typeof(GameObject),
            typeof(bool)
        })]
    static class CoreAddPrefab
    {
        static void Postfix(GameObject __result)
        {
            ThrownHits.Spawn(__result);
        }
    }

    /// <summary>
    /// What a landing or exploding thrown thing spawns by its prefab's path
    /// (ThrownHits.Spawn).
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab),
        new[]
        {
            typeof(string),
            typeof(Vector3),
            typeof(Quaternion),
            typeof(GameObject),
            typeof(bool)
        })]
    static class CoreAddPrefabByPath
    {
        static void Postfix(GameObject __result)
        {
            ThrownHits.Spawn(__result);
        }
    }

    /// <summary>
    /// Client: an explosion that catches our own player is the host's to
    /// report (through our proxy there; ThrowSync.HostReports).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.getHit),
        new[]
        {
            typeof(float),
            typeof(Transform),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    static class PlayerGetHit
    {
        static bool Prefix() => !ThrowSync.HostReports();
    }

    /// <summary>
    /// Client: so are the effects such an explosion starts on our player
    /// (ThrowSync.HostReportsEffect). Every overload of
    /// CharacterEffects.activate, as the game has them.
    /// </summary>
    [HarmonyPatch]
    static class CharacterEffectsActivate
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(CharacterEffects)).FindAll(
                m => m.Name == nameof(CharacterEffects.activate));

        static bool Prefix(CharacterEffects __instance) =>
            !ThrowSync.HostReportsEffect(__instance);
    }
}
