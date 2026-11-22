namespace DarkwoodMP.World.Places
{
    /// Another player's place against ours.
    public readonly struct SameFacts
    {
        /// They are away from the shared world (their PlayerState).
        public bool Away { get; init; }

        /// Their place (PlaceRules.Current in their game).
        public string Place { get; init; }

        /// Our player is away (PlayerCapture.LocalAway).
        public bool MeAway { get; init; }

        /// A level dream is shared (DreamSync.Shared isn't null).
        public bool DreamShared { get; init; }

        /// Our place (PlaceRules.Current).
        public string Current { get; init; }
    }
}
