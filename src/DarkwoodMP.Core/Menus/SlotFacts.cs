using System;

namespace DarkwoodMP.Menus
{
    /// One of Start Game's save slots (StartGameWindow.Slot), in the order
    /// the window lists them.
    public readonly struct SlotFacts
    {
        /// The slot's number, from 1.
        public int Id { get; init; }

        /// The slot holds a save (StartGameRules.IsUsed); else it's free.
        public bool Used { get; init; }

        /// When its save was last written: GameProfile.timeSaved read by
        /// DateTime.TryParse, or DateTime.MinValue (the zero value) when it
        /// doesn't read (StartGameWindow.SavedAt).
        public DateTime Saved { get; init; }
    }
}
