namespace DarkwoodMP.World.Mornings
{
    /// A client, when the host says the morning is over (AfterNightEnded).
    public readonly struct EndedFacts
    {
        /// There is a session and this game is a client.
        public bool IsClient { get; init; }

        /// The message is from the host (Session.HostId).
        public bool FromHost { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// An end is waiting for a dialogue to close already.
        public bool Pending { get; init; }

        /// Singleton<Controller>.Instance exists.
        public bool HasController { get; init; }

        /// We are in a morning (Controller.isAfterNight).
        public bool InMorning { get; init; }

        /// Our player dreams (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// We talk to our morning visitor (MorningRules.Talking).
        public bool Talking { get; init; }
    }
}
