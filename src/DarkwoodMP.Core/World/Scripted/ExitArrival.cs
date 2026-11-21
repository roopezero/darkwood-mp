namespace DarkwoodMP.World.Scripted
{
    /// What a game does with another player's exit from an area trigger.
    public enum ExitArrival : byte
    {
        /// Not an area trigger here, or switched off: nothing.
        Ignored = 0,

        /// Its exit ran here already since it last fired.
        RanAlready,

        /// Our player is still in it: their own exit runs it later.
        StillInside,

        /// Mark its exit run, and run its exit events.
        Run,

        /// As Run, and the trigger may fire again (it fires each time).
        RunAndRearm,
    }
}
