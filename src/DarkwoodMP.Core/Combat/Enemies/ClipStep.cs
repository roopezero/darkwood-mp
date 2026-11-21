namespace DarkwoodMP.Combat.Enemies
{
    /// What a copy's sprite animator does with the host's clip and frame
    /// (CopyStateRules.Clip).
    public enum ClipStep
    {
        /// Nothing more: no clip sent, or no library (not even pause or
        /// resume).
        Skip = 0,

        /// Another clip: the library's clip of that hash, if it has one,
        /// played from the host's frame (CopyStateRules.StartFrame).
        Switch,

        /// The same clip more than MaxFramesOff off: played again from the
        /// host's frame.
        Replay,

        /// The same clip, close enough.
        Keep,
    }
}
