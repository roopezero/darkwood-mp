namespace DarkwoodMP.Sync.Players
{
    /// One sound in a PlayerSounds message.
    public readonly struct PlayedSound
    {
        /// The game's audio id (AudioController).
        public string Id { get; init; }

        /// The volume it was played at.
        public float Volume { get; init; }
    }
}
