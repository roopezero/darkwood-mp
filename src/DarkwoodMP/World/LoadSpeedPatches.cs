using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    // LoadSpeed's patches. They run playing alone too: loading a world is
    // faster with no session as well.

    /// <summary>
    /// SaveManager.Load's loop over loadedObjects: ElementAt from an array copy
    /// (see LoadSpeed).
    /// </summary>
    [HarmonyPatch]
    static class SaveManagerLoad
    {
        static MethodBase TargetMethod()
        {
            var t =
                typeof(SaveManager).GetNestedTypes(BindingFlags.NonPublic)
                    .FirstOrDefault(n => n.Name.StartsWith("<Load>d__"));
            if (t == null)
            {
                return null;
            }

            return AccessTools.Method(t, "MoveNext");
        }

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> code) => LoadSpeed.Rewrite(code);

        static void Prefix() => LoadSpeed.Current.LoadStepping();

        static void Postfix(bool __result) =>
            LoadSpeed.Current.LoadStepped(__result);
    }

    /// <summary>
    /// The game is about to load a saved world (after its 1.6 s fade): parse
    /// the save meanwhile.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), "Start")]
    static class WorldGeneratorStart
    {
        static void Postfix() => LoadSpeed.Current.WorldStarted();
    }

    /// <summary>
    /// Core.AddPrefab(path, ...) while loading: each path's prefab is looked up
    /// once (see LoadSpeed).
    /// </summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(string),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class CoreAddPrefabCached
    {
        static bool Prefix(
            string prefab, Vector3 position, Quaternion quaternion,
            GameObject parentGO, bool worldSpace, ref GameObject __result)
        {
            LoadSpeed speed = LoadSpeed.Current;
            if (!speed.Loading)
            {
                return true;
            }

            __result = Core.AddPrefab(
                speed.Prefab(prefab), position, quaternion, parentGO,
                worldSpace);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(WorldGrid.Grid), nameof(WorldGrid.Grid.registerCullablesOnLoad))]
    static class GridRegisterCullablesOnLoad
    {
        static void Prefix() => LoadSpeed.Current.BeginBatch();
        static Exception Finalizer(Exception __exception)
        {
            LoadSpeed.Current.EndBatch();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.registerToNodes), new[]
    {
        typeof(GameObject),
        typeof(Vector2),
        typeof(WorldGrid.Cullable.Type)
    })]
    static class WorldGridRegisterToNodes
    {
        static bool Prefix(
            WorldGrid __instance, GameObject GO, Vector2 size,
            WorldGrid.Cullable.Type type) =>
            LoadSpeed.Current.GameRegistersToNodes(__instance, GO, size, type);
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.registerToClosestNode))]
    static class WorldGridRegisterToClosestNode
    {
        static bool Prefix(WorldGrid __instance, GameObject GO) =>
            LoadSpeed.Current.GameRegistersToClosestNode(__instance, GO);
    }

    [HarmonyPatch(
        typeof(WorldGrid), nameof(WorldGrid.registerToNode),
        new[] { typeof(GameObject), typeof(WorldGrid.Cullable.Type) })]
    static class WorldGridRegisterToNode
    {
        static bool Prefix(
            WorldGrid __instance, GameObject GO,
            WorldGrid.Cullable.Type type) =>
            LoadSpeed.Current.GameRegistersToNode(__instance, GO, type);
    }
}
