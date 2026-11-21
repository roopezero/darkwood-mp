namespace DarkwoodMP.World.Locks
{
    /// What a game knows when one of its locks that has a Locked (a door's,
    /// a chest's, a cabinet's) may have just opened: Locked.unlock,
    /// tryToOpenWithKey or tryToLockpick returned (the plugin's
    /// LockedUnlock, TryToOpenWithKey, TryToLockpick).
    public readonly struct UnlockSentFacts
    {
        /// This is the outermost of the patched calls
        /// (Coop.Guards.Outermost(), which the shell calls first, since it
        /// also counts the call off).
        public bool Outermost { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// Locked.locked before the call.
        public bool WasLocked { get; init; }

        /// Locked.locked after it.
        public bool Locked { get; init; }

        /// How it was opened: which patch it is (LockRules.ByStory for
        /// unlock, ByKey for tryToOpenWithKey, ByLockpick for
        /// tryToLockpick).
        public float How { get; init; }

        /// This game is a client (Session.Instance.IsClient).
        public bool Client { get; init; }

        /// The Locked has a Door.
        public bool HasDoor { get; init; }
    }
}
