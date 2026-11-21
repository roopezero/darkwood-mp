using HarmonyLib;

namespace DarkwoodMP.World.Deaths
{
    // A death's world reset, which WorldResets allows or stops. With no
    // session it always runs.

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.respawnAllEnemies))]
    static class WorldGeneratorRespawnAllEnemies
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Resets.MayRespawnEnemies();
        }
    }

    [HarmonyPatch(
        typeof(CharacterSpawner), nameof(CharacterSpawner.despawnCharacters))]
    static class CharacterSpawnerDespawnCharacters
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Resets.MayDespawnCharacters();
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.skipDay))]
    static class ControllerSkipDay
    {
        static bool Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Resets.MaySkipNight();
        }
    }
}
