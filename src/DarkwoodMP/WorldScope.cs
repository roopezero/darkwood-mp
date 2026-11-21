using System;
using UnityEngine.SceneManagement;

namespace DarkwoodMP
{
    /// <summary>
    /// State that lives as long as one loaded world. CoopSession owns it and
    /// replaces it at each world load (a chapter change loads a new world
    /// while the session goes on), and drops it when the world is left.
    /// World state moves here with its subsystem (ThrownThings so far).
    /// </summary>
    internal sealed class WorldScope : IDisposable
    {
        /// The world's player and scene: how CoopSession tells this world
        /// from the next one.
        public readonly Player Player;
        public readonly int Scene;

        /// What thrown things are doing in this world (ThrownHits).
        public readonly World.ThrownThings Thrown = new World.ThrownThings();

        public WorldScope(Player player, Scene scene)
        {
            Player = player;
            Scene = scene.handle;
        }

        /// Whether this is the world of this player in this scene.
        public bool Is(Player player, Scene scene)
        {
            return ReferenceEquals(Player, player) && Scene == scene.handle;
        }

        public void Dispose()
        {
            // Nothing yet. A subsystem's world state that moves here lets go
            // of the world's objects and events here.
        }
    }
}
