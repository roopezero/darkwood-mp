namespace DarkwoodMP.Combat.Enemies
{
    /// What decides that a thing is in an area only this client's player
    /// is in (the plugin's NetEnemies.LocalOnly).
    public readonly struct LocalFacts
    {
        /// The thing exists (its GameObject isn't null).
        public bool Exists { get; init; }

        /// Its scene is the active scene (SceneManager.GetActiveScene).
        public bool ActiveScene { get; init; }

        /// Dreams has a dreamLocation and is dreaming, prepared or
        /// switching a dream.
        public bool Dreaming { get; init; }

        /// The thing's transform is under the dream location's.
        public bool InDream { get; init; }

        /// The dream is this game's own: no shared dream
        /// (DreamSync.Shared is null), or the shared one is a prologue
        /// stage (Prologue.IsStage).
        public bool OwnDream { get; init; }

        /// Locations.HostRunsHere().
        public bool HostRunsHere { get; init; }

        /// Player.Instance isn't null.
        public bool HasPlayer { get; init; }

        /// Where the thing is.
        public Vec3 Thing { get; init; }

        /// Where our player is.
        public Vec3 Us { get; init; }
    }
}
