namespace DarkwoodMP.World.Locks
{
    /// What a game knows when one of its combination padlocks may have
    /// just opened: Padlock.unlock(manually) returned (the plugin's
    /// PadlockUnlock patch).
    public readonly struct PadlockSentFacts
    {
        /// This is the outermost of the patched calls
        /// (Coop.Guards.Outermost(), called first).
        public bool Outermost { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// Padlock.locked before the call.
        public bool WasLocked { get; init; }

        /// Padlock.locked after it.
        public bool Locked { get; init; }

        /// The call's manually: a player entered a code (false: the story
        /// opened it).
        public bool Manually { get; init; }

        /// Padlock.currentCombination, the code entered.
        public string Code { get; init; }
    }
}
