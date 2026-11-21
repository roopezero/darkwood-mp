namespace DarkwoodMP.World.Scripted
{
    /// What a game does with a world event a character's animation fires.
    public enum AnimationEvent : byte
    {
        /// The game's own call runs, and nothing is sent.
        Run = 0,

        /// Client: left to the host, whose copy plays the same animation.
        LeaveToHost,

        /// Host: send it as kind 2, then the game's own call runs.
        SendAndRun,
    }
}
