namespace DarkwoodMP.World.Story
{
    /// Where the game's text for a "journal updated" popup is looked for
    /// (JournalRules.TextOf). The popup is shown when the key is not empty
    /// and Language has a text that isn't empty for it in Sheet, or in
    /// OtherSheet when there is one.
    public readonly struct PopupText
    {
        /// The popup carries its own text (reputation and others): shown
        /// with no lookup.
        public bool Always { get; init; }

        /// The language key.
        public string Key { get; init; }

        /// The language sheet.
        public string Sheet { get; init; }

        /// A second sheet to look in when Sheet hasn't got it; null for
        /// none.
        public string OtherSheet { get; init; }
    }
}
