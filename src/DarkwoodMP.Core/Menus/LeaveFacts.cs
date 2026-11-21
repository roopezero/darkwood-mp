using System;

namespace DarkwoodMP.Menus
{
    /// What the question before leaving a world looks at
    /// (MenuList.LeaveText): "Back to main menu" and "Quit" in the pause
    /// menu.
    public readonly struct LeaveFacts
    {
        /// The world is the player's copy of the host's
        /// (ModHost.Share.PlayingHostCopy; false with no WorldShare).
        public bool HostCopy { get; init; }

        /// The time of the last save could be read: false when reading
        /// Singleton<SaveManager>.Instance.lastTimeSaved threw (no
        /// SaveManager).
        public bool SaveKnown { get; init; }

        /// The wall clock now (DateTime.Now).
        public DateTime Now { get; init; }

        /// When the game last saved (SaveManager.lastTimeSaved).
        public DateTime Saved { get; init; }

        /// The player hosts a session (Session.IsHost; false with none).
        public bool Host { get; init; }

        /// How many players are in the host's game besides the host
        /// (Session.Peers.Count).
        public int Peers { get; init; }

        /// The player is in someone else's game (Session.IsClient).
        public bool Client { get; init; }
    }
}
