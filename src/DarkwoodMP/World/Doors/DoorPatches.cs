using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Doors
{
    // Each prefix counts one patched world call deeper and keeps the door
    // as it was (Doors.Before); each postfix hands the call to the
    // session's Doors, which sends it if it changed the door.

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorOpen
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(
            Door __instance,
            Vector3 openerPosition,
            Transform openerTransform,
            DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                var at = Doors.Opener(openerPosition, openerTransform);
                coop.Doors.Opened(__instance, __state, at);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.close))]
    static class DoorClose
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(Door __instance, DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Closed(__instance, __state);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.barricade))]
    static class DoorBarricade
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(Door __instance, bool byPlayer, DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Barricaded(__instance, __state, byPlayer);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyBarricade))]
    static class DoorDestroyBarricade
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(Door __instance, bool silent, DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.BarricadeBroken(__instance, __state, silent);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyDoor))]
    static class DoorDestroyDoor
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(Door __instance, bool silently, DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Broken(__instance, __state, silently);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unDestroy))]
    static class DoorUnDestroy
    {
        static void Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
        }

        static void Postfix(Door __instance, DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Mended(__instance, __state);
            }
        }
    }

    /// <summary>
    /// Damage to a door or its barricade. Only the hit is sent (not what it
    /// causes): the other games make the same call, which takes off the same
    /// health and breaks the barricade or door when it runs out, as it does
    /// here.
    /// </summary>
    [HarmonyPatch(
        typeof(Door), nameof(Door.getHit),
        new[] { typeof(int), typeof(Transform), typeof(bool), typeof(bool) })]
    static class DoorGetHit
    {
        /// <summary>
        /// On a client, a hit the host makes too (an explosion, fire) isn't
        /// made here: the host's DoorHit is.
        /// </summary>
        static bool Prefix(Door __instance, out DoorState __state)
        {
            __state = Doors.Before(__instance);
            return !BreakSync.HostsHit();
        }

        static void Postfix(
            Door __instance,
            int damage,
            bool normalHit,
            bool canDamageMetal,
            DoorState __state)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                var hit = new ActionValues
                {
                    X = damage,
                    F1 = normalHit,
                    F2 = canDamageMetal,
                };

                coop.Doors.Hit(__instance, __state, hit);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.lockMe))]
    static class DoorLockMe
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Door __instance, string keyType)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Locked(__instance, keyType);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unlock))]
    static class DoorUnlock
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Door __instance)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Unlocked(__instance);
            }
        }
    }

    /// <summary>
    /// The story clearing a door's "blocked" (GameEvent modifyDoor unblock),
    /// which a player can't open through until then (Player.itemDefaultAction
    /// says "blocked"). That step is the host's (ScriptedEvents.HostSteps), so
    /// the host sends it to everyone; without this the joiners' copies stayed
    /// blocked for good (Piotrek's door). Door.unblock only sets a field, so
    /// Mono inlines it into its caller and a patch on it never runs (tried: the
    /// host unblocked, nothing was sent). The step's coroutine (GameEvent.fire,
    /// the only caller) calls DoorUnblock.Unblock instead, by a transpiler.
    /// </summary>
    [HarmonyPatch]
    static class DoorUnblock
    {
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
                    if (m != null)
                    {
                        yield return m;
                    }
                }
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var unblock =
                AccessTools.Method(typeof(Door), nameof(Door.unblock));
            var ours =
                AccessTools.Method(typeof(DoorUnblock), nameof(Unblock));
            int swapped = 0;
            foreach (var ci in instructions)
            {
                if (ci.Calls(unblock))
                {
                    yield return new CodeInstruction(OpCodes.Call, ours)
                    {
                        labels = ci.labels,
                        blocks = ci.blocks
                    };

                    swapped++;
                }
                else
                {
                    yield return ci;
                }
            }

            if (swapped == 0)
            {
                Plugin.Log.LogWarning(
                    "DoorUnblock: no Door.unblock in GameEvent.fire (game "
                        + "changed?)");
            }
        }

        /// <summary>
        /// Door.unblock, and sent to the others when it cleared the block here.
        /// </summary>
        public static void Unblock(Door door)
        {
            bool was = door.blocked;
            door.unblock();
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Doors.Unblocked(door, was);
            }
        }
    }
}
