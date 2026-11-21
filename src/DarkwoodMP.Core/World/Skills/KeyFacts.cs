namespace DarkwoodMP.World.Skills
{
    /// What decides whether the Skills panel's key is read this frame.
    public readonly struct KeyFacts
    {
        /// The co-op menu (F7) shows.
        public bool CoopMenuOpen { get; init; }

        /// An IMGUI control has the keyboard (GUIUtility.keyboardControl
        /// isn't 0).
        public bool KeyboardTaken { get; init; }

        /// The key is set to KeyCode.None, which turns it off.
        public bool Off { get; init; }
    }
}
