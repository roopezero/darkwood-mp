namespace DarkwoodMP.Sync.Players
{
    /// A prefab the game just added, for whether it's our player's blood
    /// (PlayerBloodPatch.Postfix).
    public readonly struct BloodFacts
    {
        /// How deep we are in Player.getHit (PlayerFx.InGetHit).
        public int InGetHit { get; init; }

        /// Core.AddPrefab returned an object.
        public bool HasResult { get; init; }

        /// The prefab's path; null when the game passed none.
        public string Prefab { get; init; }

        /// PlayerFx.Instance isn't null and is sharing.
        public bool Sharing { get; init; }

        /// WorldActions.Replaying.
        public bool Replaying { get; init; }
    }
}
