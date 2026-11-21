namespace DarkwoodMP.Sync.Players
{
    /// A sound the game just played, for whether our player made it
    /// (PlayerFx.IsOurs).
    public readonly struct SoundSourceFacts
    {
        /// Player.Instance isn't null.
        public bool HasPlayer { get; init; }

        /// WorldActions.Replaying.
        public bool Replaying { get; init; }

        /// The sound has a parent object.
        public bool HasParent { get; init; }

        /// The parent is our player's transform or under it.
        public bool ParentIsOurs { get; init; }

        /// How deep we are in our player's own methods
        /// (PlayerFx.InPlayerCode).
        public int InPlayerCode { get; init; }

        /// WorldActions.Depth: a shared world action is running.
        public int Depth { get; init; }
    }
}
