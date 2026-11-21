namespace DarkwoodMP.Combat.Nights
{
    /// Host: the spawner and the host's player when the night's attackers
    /// are due (NightSpawns.NightChars). Places are numbered by the shell
    /// (a Location's instance id), and only compared.
    public readonly struct NightFacts
    {
        /// There is a CharacterSpawner.
        public bool HasSpawner { get; init; }

        /// CharacterSpawner.spawnNocturnalCharacters.
        public bool SpawnsNocturnal { get; init; }

        /// The host's player is dreaming (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// The host's player is in an outside location
        /// (OutsideLocations.playerInOutsideLocation).
        public bool HostOutside { get; init; }

        /// The host's player has a big location (whereAmI.bigLocation).
        public bool HostAtPlace { get; init; }

        /// That location.
        public int HostPlace { get; init; }

        /// That location is a hideout (Location.playerBase).
        public bool HostBase { get; init; }
    }
}
