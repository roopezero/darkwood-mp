using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    // A joiner in the shared world doesn't roll what the host rolls
    // (WorldEvents.Follows); the dawn's objects and the weather are the
    // host's on a joiner away or not (WorldEvents.Client). With no session
    // every patch here leaves the game as it is.

    [HarmonyPatch(
        typeof(NightScenarios), nameof(NightScenarios.setCurrentScenario))]
    static class NightScenariosSetCurrentScenario
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Follows;
        }
    }

    [HarmonyPatch(
        typeof(NightScenario), nameof(NightScenario.checkFrequencies))]
    static class NightScenarioCheckFrequencies
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Follows;
        }
    }

    [HarmonyPatch(typeof(NightScenario), nameof(NightScenario.fireEvent))]
    static class NightScenarioFireEvent
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Follows;
        }
    }

    /// <summary>
    /// The day's random events (a redneck ambush, world events) at their random
    /// times.
    /// </summary>
    [HarmonyPatch(typeof(Events), "onUpdateTime")]
    static class EventsOnUpdateTime
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Follows;
        }
    }

    /// <summary>
    /// A joiner's own random events don't fire; a night event the host sent
    /// plays only its location events' presentation.
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RandomEventFireFollow
    {
        static bool Prefix(RandomEvent __instance)
        {
            CoopSession coop = Coop.Current;
            return coop == null || coop.WorldEvents.RunsRandomEvent(__instance);
        }
    }

    /// <summary>Host: a night event fired; send it.</summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class CustomEventFireSend
    {
        static void Postfix(CustomEvent __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.WorldEvents.SendNightEvent(__instance);
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
    static class GameEventFireNightStep
    {
        static void Postfix(GameEvent __instance, ref IEnumerator __result)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __result = coop.WorldEvents.Step(__instance, __result);
            }
        }
    }

    /// <summary>
    /// The shadows' lights: the host picks the lamp and sends it; joiners wait
    /// for it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.shadowLightsTurnOff))]
    static class PlayerShadowLightsTurnOff
    {
        static bool Prefix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            return coop == null
                || coop.WorldEvents.ShadowLightsTurnOff(__instance);
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.spawnRandomObjects))]
    static class WorldGeneratorSpawnRandomObjects
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Client;
        }

        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.WorldEvents.RandomObjectsMade();
            }
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.despawnRandomObjects))]
    static class WorldGeneratorDespawnRandomObjects
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Client;
        }
    }

    /// <summary>Night mushrooms: made on the host and sent.</summary>
    [HarmonyPatch(typeof(WormsSpawner), nameof(WormsSpawner.spawn))]
    static class WormsSpawnerSpawn
    {
        static bool Prefix(out NightCount __state)
        {
            __state = new NightCount(null, 0);
            CoopSession coop = Coop.Current;
            return coop == null
                || coop.WorldEvents.NightObjectBefore(out __state);
        }

        static void Postfix(NightCount __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.WorldEvents.NightObjectAfter(__state);
            }
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.onNewDay))]
    static class RainOnNewDay
    {
        static bool Prefix(Rain __instance)
        {
            CoopSession coop = Coop.Current;
            return coop == null || coop.WorldEvents.RainNewDay(__instance);
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpNextRain))]
    static class RainSetUpNextRain
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Client;
        }
    }

    [HarmonyPatch(typeof(Rain), nameof(Rain.setUpFog))]
    static class RainSetUpFog
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            return coop == null || !coop.WorldEvents.Client;
        }
    }

    /// <summary>
    /// Host: the day's redneck ambush can be any player's
    /// (WorldEvents.Ambush).
    /// </summary>
    [HarmonyPatch(
        typeof(RandomEvent), nameof(RandomEvent.fire),
        new[] { typeof(bool), typeof(bool) })]
    static class RandomEventFireAmbush
    {
        static bool Prefix(
            RandomEvent __instance, bool checkIfRequirementsMet, bool force)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            var call = new AmbushGateFacts
            {
                Force = force,
                CheckRequirements = checkIfRequirementsMet,
            };

            return coop.WorldEvents.Ambush(__instance, call);
        }
    }
}
