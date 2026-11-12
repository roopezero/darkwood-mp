using DarkwoodMP.Combat;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Dying, with others in the world. Darkwood's death (Player.onDeath) takes the player home, leaves half their
    /// items in a bag where they fell (shared: see Containers.SharedPrefabs), and then resets the world: every enemy
    /// is respawned, and on a hard night the rest of it is skipped. With others playing, that's not one player's
    /// to decide:
    ///
    ///   a client's death never does it (the host runs the enemies and the clock; doing it here would only tear
    ///   down the host's enemies it's showing);
    ///   the host's own death doesn't either while others are in the world, who may still be fighting; it does as
    ///   before when the host is alone.
    /// </summary>
    static class DeathRules
    {
        public static bool WorldResetAllowed()
        {
            if (NetEnemies.ClientMode) return false;
            var s = Net.Session.Instance;
            if (s != null && s.IsHost && s.Peers.Count > 0 && InPlayerDeath()) return false;
            return true;
        }

        /// <summary>Whether we're inside Player.onDeath (a coroutine: its body is a nested type's MoveNext).</summary>
        private static bool InPlayerDeath()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 16; i++)
            {
                var t = st.GetFrame(i)?.GetMethod()?.DeclaringType;
                if (t != null && t.DeclaringType == typeof(Player) && t.Name.Contains("onDeath")) return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.respawnAllEnemies))]
    static class RespawnAllEnemiesPatch
    {
        static bool Prefix()
        {
            bool ok = DeathRules.WorldResetAllowed();
            if (!ok) Plugin.Log.LogInfo("A death didn't respawn the world's enemies: others are playing in it");
            return ok;
        }
    }

    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.despawnCharacters))]
    static class DespawnCharactersPatch
    {
        static bool Prefix() => DeathRules.WorldResetAllowed();
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.skipDay))]
    static class SkipDayPatch
    {
        static bool Prefix()
        {
            bool ok = DeathRules.WorldResetAllowed();
            if (!ok) Plugin.Log.LogInfo("A death didn't skip the night: others are playing in it");
            return ok;
        }
    }
}
