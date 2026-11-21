namespace DarkwoodMP.Combat.Nights
{
    /// The night and the host's player at a tick of the worm loop
    /// (NightSpawns.WormLoop).
    public readonly struct WormFacts
    {
        /// A client in the host's world (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// The loop's CharacterSpawner is still there.
        public bool HasSpawner { get; init; }

        /// The host's player is there (Player.Instance).
        public bool HasHost { get; init; }

        /// Controller.isHardNight.
        public bool HardNight { get; init; }

        /// Core.isDay().
        public bool Day { get; init; }

        /// The host's player is dreaming (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// Player.ignoreNightSickness.
        public bool IgnoresSickness { get; init; }

        /// The host's player has a shadow ward
        /// (CharacterEffectType.shadowWard).
        public bool HostWard { get; init; }
    }
}
