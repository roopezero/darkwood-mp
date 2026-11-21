using System.Collections;
using HarmonyLib;

namespace DarkwoodMP.Combat
{
    // These run with no session too: the game's worm coroutine is always
    // replaced (alone it sends worms at our player only), and the test dummy
    // brings a stand-in into a game played alone.

    /// <summary>
    /// Joiners send nobody at night: the host does it for every player, a
    /// budget per occupied hideout.
    /// </summary>
    [HarmonyPatch(typeof(CharacterSpawner), "spawnNightChar")]
    static class CharacterSpawnerSpawnNightChar
    {
        static bool Prefix(CharacterSpawner __instance) =>
            NightSpawns.Nights.NightCharDue(__instance);
    }

    /// <summary>
    /// A night attacker gone (dead, or despawned) is one fewer out at the
    /// hideout it was sent to.
    /// </summary>
    [HarmonyPatch(
        typeof(CharacterSpawner),
        nameof(CharacterSpawner.removeFromNightChars))]
    static class CharacterSpawnerRemoveFromNightChars
    {
        static void Prefix(Character _character) =>
            NightSpawns.Nights.Removed(_character);
    }

    /// <summary>
    /// At dawn the scenarios start again (NightScenarios.resetScenarios), and
    /// so does every hideout's budget.
    /// </summary>
    [HarmonyPatch(
        typeof(NightScenarios), nameof(NightScenarios.resetScenarios))]
    static class NightScenariosResetScenarios
    {
        static void Postfix() => NightSpawns.Nights.NewDay();
    }

    /// <summary>
    /// The worm coroutine is replaced by NightSpawner.WormLoop (the same rule,
    /// for each player).
    /// </summary>
    [HarmonyPatch(typeof(CharacterSpawner), "waitToSpawnWorm")]
    static class CharacterSpawnerWaitToSpawnWorm
    {
        static bool Prefix(
            CharacterSpawner __instance, ref IEnumerator __result)
        {
            __result = NightSpawns.Nights.WormLoop(__instance);
            return false;
        }
    }
}
