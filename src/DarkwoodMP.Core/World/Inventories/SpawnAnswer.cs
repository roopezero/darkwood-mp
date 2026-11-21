namespace DarkwoodMP.World.Inventories
{
    /// What to do with something another game made (Containers.OnSpawn).
    public readonly struct SpawnAnswer
    {
        public SpawnStep Step { get; init; }

        /// Why it's refused, when Step is Refuse.
        public Verdict Verdict { get; init; }

        /// The host counts one placed thing off the client's character
        /// (Characters.Placed). This is decided before the prefab is looked
        /// for, so it holds even when NoSuchPrefab then refuses it.
        public bool CountsPlaced { get; init; }

        /// It's what a thrown item left behind, on a client: not saved, and
        /// its flames count as thrown (ThrownHits.Spawned).
        public bool Thrown { get; init; }
    }
}
