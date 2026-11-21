using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    // ---- joiners don't roll -------------------------------------------------

    [HarmonyPatch(
        typeof(NightScenarios), nameof(NightScenarios.setCurrentScenario))]
    static class ScenarioChoicePatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    [HarmonyPatch(
        typeof(NightScenario), nameof(NightScenario.checkFrequencies))]
    static class NightEventRollPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    [HarmonyPatch(typeof(NightScenario), nameof(NightScenario.fireEvent))]
    static class NightEventPickPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    /// <summary>
    /// The day's random events (a redneck ambush, world events) at their random
    /// times.
    /// </summary>
    [HarmonyPatch(typeof(Events), "onUpdateTime")]
    static class DayEventsPatch
    {
        static bool Prefix() => !WorldEvents.Follows;
    }

    /// <summary>
    /// A joiner's own random events don't fire; a night event the host sent
    /// plays only its location events' presentation.
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RandomEventPatch
    {
        static bool Prefix(RandomEvent __instance) =>
            !WorldEvents.Follows
                || (WorldEvents.Presentation > 0
                    && __instance.type == RandomEvent.Type.locationEvent);
    }

    /// <summary>Host: a night event fired; send it.</summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class NightEventSendPatch
    {
        static void Postfix(CustomEvent __instance)
        {
            WorldEvents instance = WorldEvents.Instance;
            if (instance != null)
            {
                instance.SendNightEvent(__instance);
            }
        }
    }

    /// <summary>
    /// A joiner playing the host's night event runs only the steps that are
    /// seen or heard.
    /// </summary>
    [HarmonyPatch(
        typeof(GameEvent), nameof(GameEvent.fire),
        new[] { typeof(GameObject) })]
    static class PresentationPatch
    {
        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            if (WorldEvents.Presentation <= 0 || !WorldEvents.Follows)
            {
                return;
            }

            if (WorldEvents.PresentationOnly(__instance))
            {
                __result = WorldEvents.AsPresentation(__result);
            }
            else
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night event step {__instance.type} "
                            + $"({__instance.Value}) left to the host");
                }

                __result = WorldEvents.Nothing();
            }
        }
    }

    /// <summary>
    /// The shadows' lights: the host picks the lamp and sends it; joiners wait
    /// for it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.shadowLightsTurnOff))]
    static class ShadowLightsPatch
    {
        static bool Prefix(Player __instance)
        {
            if (WorldEvents.Follows)
            {
                return false;
            }

            if (Session.Instance != null && Session.Instance.IsHost
                && Coop.Guards.Sharing && WorldEvents.Instance != null)
            {
                WorldEvents.Instance.HostShadowLights(__instance);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.spawnRandomObjects))]
    static class RandomObjectsPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
        static void Postfix()
        {
            if (!NetEnemies.ClientMode)
            {
                WorldEvents instance = WorldEvents.Instance;
                if (instance != null)
                {
                    instance.RandomObjectsMade();
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.despawnRandomObjects))]
    static class RandomObjectsGonePatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    /// <summary>Night mushrooms: made on the host and sent.</summary>
    [HarmonyPatch(typeof(WormsSpawner), nameof(WormsSpawner.spawn))]
    static class NightObjectPatch
    {
        static bool Prefix(out (WorldChunk chunk, int count) __state)
        {
            __state = (null, 0);
            if (WorldEvents.Follows)
            {
                return false;
            }

            var p = Player.Instance;
            // The game lists the new mushroom in the chunk the player is in.
            WorldChunk chunk = null;
            if (p != null)
            {
                chunk = WorldChunk.getChunkAtPos(p._transform.position);
            }

            if (chunk != null)
            {
                __state = (chunk, chunk.nightSpawnedObjects.Count);
            }

            return true;
        }

        static void Postfix((WorldChunk chunk, int count) __state)
        {
            if (__state.chunk == null)
            {
                return;
            }

            var list = __state.chunk.nightSpawnedObjects;
            var big = Player.Instance.whereAmI.bigLocation;
            if (list.Count > __state.count && big != null
                && big.nightMushroom != null)
            {
                WorldEvents instance = WorldEvents.Instance;
                if (instance != null)
                {
                    instance.SendNightObject(
                        list[list.Count - 1], big.nightMushroom.name);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.onNewDay))]
    static class RainDayPatch
    {
        /// <summary>
        /// A joiner keeps the new day's fog reset (not random); the rest is the
        /// host's, in Weather.
        /// </summary>
        static bool Prefix(Rain __instance)
        {
            if (!NetEnemies.ClientMode)
            {
                return true;
            }

            __instance.fogFadedInToday = false;
            __instance.fogFadedOutToday = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpNextRain))]
    static class RainNextPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpFog))]
    static class FogPatch
    {
        static bool Prefix() => !NetEnemies.ClientMode;
    }

    /// <summary>
    /// Host: the day's redneck ambush (RandomEvent spawnRedneck) can be any
    /// player's. The game sends one redneck at the host's player when it's
    /// outdoors, away from any location, from day 2. With others playing, the
    /// ambush picks at random among every player that holds for (the host's
    /// player, and each living joiner's proxy that is outdoors and away from
    /// any location); for a joiner the redneck is spawned around their proxy
    /// and goes for them, as the game's does for the host. It's the host's
    /// enemy like any other, so it reaches the joiners as an EnemySpawn.
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RedneckAmbushPatch
    {
        private static readonly System.Reflection.MethodInfo RemoveMe =
            AccessTools.Method(typeof(RandomEvent), "removeMe");

        static bool Prefix(
            RandomEvent __instance, bool checkIfRequirementsMet, bool force)
        {
            var e = __instance;
            if (NetEnemies.ClientMode || !Threat.Enabled
                || e.type != RandomEvent.Type.spawnRedneck)
            {
                return true;
            }

            // The game's own gate (it returns before doing anything): left to
            // it.
            bool wants = (!e.disabled || force)
                && ((((checkIfRequirementsMet && e.requirementsMet())
                            || !checkIfRequirementsMet)
                        && (!e.startedToday || e.multipleTimesPerDay))
                    || force);
            if (!wants)
            {
                return true;
            }

            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || ctl.day < 2)
            {
                return true;
            }

            var host = Player.Instance;
            bool hostCan = host != null && host.alive && !host.isInside
                && host.whereAmI != null && host.whereAmI.bigLocation == null;
            var joiners = new List<NetPlayerProxy>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p != null && p.alive
                    && !JoinerAi.InsideAt(p.transform.position)
                    && NightSpawns.BigLocationAt(p.transform.position) == null)
                {
                    joiners.Add(p);
                }
            }

            if (joiners.Count == 0)
            {
                // only the host's player could be ambushed: the game's own
                return true;
            }

            int hostSlot = 0;
            if (hostCan)
            {
                hostSlot = 1;
            }

            int pick = UnityEngine.Random.Range(0, joiners.Count + hostSlot);
            if (pick >= joiners.Count)
            {
                return true; // the host's player: the game's own
            }

            var proxy = joiners[pick];
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                proxy.gameObject, Vector3.zero, 1500f, "Redneck02",
                nocturnal: false, attackPlayer: false, relentlessPursuit: true);
            if (c != null)
            {
                if (c.aggressiveness != Aggressiveness.defensive)
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                c.attackCharacter(proxy.transform);
                c.attackType = AttackType.hitAndRun;
                c.wantToDespawn = true;
                c.maxStamina = 1000f;
                float distance = Core.trueDistance(
                    c.transform.position, proxy.transform.position);
                Plugin.Log.LogInfo(
                    $"[events] the day's redneck ambush is {proxy.OwnerName}'s "
                        + $"({distance:0} from them)");
            }

            e.randomizeStartTime();
            if (c != null)
            {
                e.startedToday = true;
                if (e.removeOnFire)
                {
                    System.Reflection.MethodInfo removeMe = RemoveMe;
                    if (removeMe != null)
                    {
                        removeMe.Invoke(e, null);
                    }
                }
            }

            return false;
        }
    }
}
