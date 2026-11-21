namespace DarkwoodMP.Combat.Enemies
{
    /// A sound of the host's enemy, as EnemySound carries it.
    public readonly struct EnemySound
    {
        public uint Id { get; init; }

        /// The AudioController id; "" when its loop stopped.
        public string Audio { get; init; }

        public float Volume { get; init; }
    }
}
