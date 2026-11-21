namespace DarkwoodMP.Sync.Players
{
    /// A name tag's words, and whether the dummy's damage total goes back
    /// to 0.
    public readonly struct NameTag
    {
        public string Text { get; init; }

        /// The dummy's flash is over: its total is 0 from now on.
        public bool ResetsDamage { get; init; }
    }
}
