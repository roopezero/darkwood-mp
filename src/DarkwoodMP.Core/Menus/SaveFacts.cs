namespace DarkwoodMP.Menus
{
    /// One entry of the game's list of saves, as Start Game reads it
    /// (SaveManager.loadGameProfiles().profiles, in StartGameWindow.Refresh).
    public readonly struct SaveFacts
    {
        /// The list has a profile there (not null).
        public bool Present { get; init; }

        /// The profile is in use (GameProfile.Active).
        public bool Active { get; init; }

        /// Its character died for good (GameProfile.dead).
        public bool Dead { get; init; }
    }
}
