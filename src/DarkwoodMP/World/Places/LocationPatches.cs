using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Places
{
    /// <summary>
    /// Host: a location loaded for the joiners doesn't take our player's
    /// grid (Locations.AfterAddedToGrid).
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.addObjectsToGrid))]
    static class LocationAddObjectsToGrid
    {
        static void Postfix(Location __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Locations.AfterAddedToGrid(__instance);
            }
        }

    }

    /// <summary>
    /// Host: a location kept running for a joiner isn't switched off when our
    /// player leaves it.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.leave))]
    static class LocationLeave
    {
        static bool Prefix(Location __instance, bool force)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Locations.Kept.BeforeLeave(__instance, force);
        }

    }

    /// <summary>
    /// Host: our player going into a location already running for a joiner gets
    /// its enter events.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.enter))]
    static class LocationEnter
    {
        static void Prefix(Location __instance, bool force)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Locations.Kept.BeforeEnter(__instance, force);
            }
        }

    }

    /// <summary>
    /// Host: a location entered for a joiner doesn't run its enter events here
    /// (the joiner's game does).
    /// </summary>
    [HarmonyPatch(typeof(Location), "checkEnterEvents")]
    static class LocationCheckEnterEventsKept
    {
        static bool Prefix(Location __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Locations.Kept.BeforeEnterEvents(__instance);
        }

    }

    /// <summary>
    /// Host: the save records a location kept running only for a joiner as our
    /// player left it (entered false), as the game would have it: entered says
    /// our own player went in. Saved as entered, a reload that finds the
    /// location switched off leaves it marked entered, and our player's next
    /// visit (Location.enter, which does nothing for an entered location) would
    /// find it empty.
    /// </summary>
    [HarmonyPatch(
        typeof(Location.SaveState), MethodType.Constructor,
        new[] { typeof(Location) })]
    static class LocationSaveStateCtor
    {
        static void Postfix(Location.SaveState __instance, Location location)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locations.Kept.AfterSave(__instance, location);
        }
    }

    /// <summary>
    /// A client asks the host where a location stands a little early: when the
    /// trip to it starts.
    /// </summary>
    [HarmonyPatch(
        typeof(OutsideLocations), nameof(OutsideLocations.prepareLocation),
        new[] { typeof(string), typeof(Transform) })]
    static class OutsideLocationsPrepareLocation
    {
        static void Prefix(OutsideLocations __instance, string locationName)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Locations.BeforePrepare(__instance, locationName);
        }
    }

    /// <summary>
    /// A client loads a location only once the host has said where it stands
    /// (or didn't, in time).
    /// </summary>
    [HarmonyPatch(
        typeof(OutsideLocations), nameof(OutsideLocations.spawnLocation),
        new[] { typeof(string), typeof(bool) })]
    static class OutsideLocationsSpawnLocation
    {
        static bool Prefix(
            OutsideLocations __instance, string locationName,
            bool transportAfterSpawn, ref IEnumerator __result)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            IEnumerator instead = coop.Locations.SpawnInstead(
                __instance, locationName, transportAfterSpawn);
            if (instead == null)
            {
                return true;
            }

            __result = instead;
            return false;
        }
    }

    /// <summary>
    /// spawnLocation's body: the spot it reads from locationPositions goes
    /// through PositionFor.
    /// </summary>
    [HarmonyPatch]
    static class OutsideLocationsSpawnLocationBody
    {
        static Vector3 PositionFor(Vector3 picked, string name)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return picked;
            }

            return coop.Locations.PositionFor(picked, name);
        }

        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(
                AccessTools.Method(
                    typeof(OutsideLocations),
                    nameof(OutsideLocations.spawnLocation),
                    new[] { typeof(string), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var getItem = AccessTools.Method(typeof(List<Vector3>), "get_Item");
            var nameField =
                AccessTools.Field(original.DeclaringType, "locationName");
            if (nameField == null)
            {
                throw new System.Exception(
                    "no locationName in spawnLocation's body");
            }

            var list = code.ToList();
            int i = list.FindIndex(c => c.Calls(getItem));
            if (i < 0)
            {
                throw new System.Exception(
                    "locationPositions[...] not found in spawnLocation's body");
            }

            list.InsertRange(i + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, nameField),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(
                        typeof(OutsideLocationsSpawnLocationBody),
                        nameof(PositionFor))),
            });

            return list;
        }
    }

    /// <summary>
    /// LocationMarker.spawnLocation's body: the location's random turn goes
    /// through RotationFor.
    /// </summary>
    [HarmonyPatch]
    static class LocationMarkerSpawnLocationBody
    {
        /// The game's roll is always made, so its dice advance as they did.
        static int RotationFor(LocationMarker m)
        {
            int rolled = Core.getRandomHalfRotation();
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return rolled;
            }

            return coop.Locations.RotationFor(m, rolled);
        }

        static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(
                AccessTools.Method(
                    typeof(LocationMarker),
                    nameof(LocationMarker.spawnLocation),
                    new[] { typeof(GameObject), typeof(bool) }));

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var roll = AccessTools.Method(
                typeof(Core), nameof(Core.getRandomHalfRotation));
            var self = AccessTools.Field(original.DeclaringType, "<>4__this");
            if (self == null)
            {
                throw new System.Exception(
                    "no <>4__this in LocationMarker.spawnLocation's body");
            }

            var list = code.ToList();
            int done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(roll))
                {
                    continue;
                }

                var labels = list[i].labels;
                list[i] = new CodeInstruction(OpCodes.Ldarg_0)
                {
                    labels = labels,
                    blocks = list[i].blocks
                };

                list.InsertRange(i + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldfld, self),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(LocationMarkerSpawnLocationBody),
                            nameof(RotationFor))),
                });

                done++;
            }

            if (done == 0)
            {
                throw new System.Exception(
                    "getRandomHalfRotation not found in "
                        + "LocationMarker.spawnLocation's body");
            }

            return list;
        }
    }
}
