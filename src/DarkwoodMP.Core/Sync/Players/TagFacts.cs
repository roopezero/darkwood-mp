namespace DarkwoodMP.Sync.Players
{
    /// What is over the world when name tags would be drawn
    /// (PlayerSync.DrawNameTags).
    public readonly struct TagFacts
    {
        /// The pause menu or one of the mod's windows (UiState.MenuOver).
        public bool MenuOver { get; init; }

        /// The game's map is open (MapMarkers.MapOpen).
        public bool MapOpen { get; init; }

        /// A cutscene, a game menu or anything else that isn't the world in
        /// play (UiState.WorldOverlaysHidden).
        public bool OverlaysHidden { get; init; }
    }
}
