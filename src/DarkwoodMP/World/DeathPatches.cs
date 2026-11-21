using DarkwoodMP.Combat;
using DarkwoodMP.World.Deaths;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Dying, with others in the world. Darkwood's death (Player.onDeath) takes
    /// the player home, leaves half their items in a bag where they fell
    /// (shared: see Containers.SharedPrefabs), and then resets the world: every
    /// enemy is respawned, and on a hard night the rest of it is skipped. With
    /// others playing, that's not one player's to decide:
    ///
    ///   a client's death never does it (the host runs the enemies and the
    ///   clock; doing it here would only tear down the host's enemies it's
    ///   showing);
    ///   the host's own death doesn't either while others are in the world, who
    ///   may still be fighting; it does when the host is alone.
    ///
    /// The decision is the core's DeathRules.
    /// </summary>
    static class WorldResets
    {
        public static bool WorldResetAllowed()
        {
            DeathFacts facts = FactsOf(Net.Session.Instance);
            if (DeathRules.NeedsStack(facts))
            {
                facts = new DeathFacts
                {
                    ClientMode = facts.ClientMode,
                    IsHost = facts.IsHost,
                    Peers = facts.Peers,
                    InDeath = InPlayerDeath(),
                };
            }

            return DeathRules.WorldResetAllowed(facts);
        }

        private static DeathFacts FactsOf(Net.Session s)
        {
            bool host = s != null && s.IsHost;
            int peers = 0;
            if (s != null)
            {
                peers = s.Peers.Count;
            }

            return new DeathFacts
            {
                ClientMode = NetEnemies.ClientMode,
                IsHost = host,
                Peers = peers,
            };
        }

        /// <summary>
        /// Whether we're inside Player.onDeath (a coroutine: its body is a
        /// nested type's MoveNext).
        /// </summary>
        private static bool InPlayerDeath()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 16; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                System.Type t = null;
                if (frame != null)
                {
                    System.Reflection.MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        t = method.DeclaringType;
                    }
                }

                if (t != null && t.DeclaringType == typeof(Player)
                    && t.Name.Contains("onDeath"))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(WorldGenerator), nameof(WorldGenerator.respawnAllEnemies))]
    static class RespawnAllEnemiesPatch
    {
        static bool Prefix()
        {
            bool ok = WorldResets.WorldResetAllowed();
            if (!ok)
            {
                Plugin.Log.LogInfo(
                    "A death didn't respawn the world's enemies: others are "
                        + "playing in it");
            }

            return ok;
        }
    }

    [HarmonyPatch(
        typeof(CharacterSpawner), nameof(CharacterSpawner.despawnCharacters))]
    static class DespawnCharactersPatch
    {
        static bool Prefix() => WorldResets.WorldResetAllowed();
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.skipDay))]
    static class SkipDayPatch
    {
        static bool Prefix()
        {
            bool ok = WorldResets.WorldResetAllowed();
            if (!ok)
            {
                Plugin.Log.LogInfo(
                    "A death didn't skip the night: others are playing in it");
            }

            return ok;
        }
    }
}
