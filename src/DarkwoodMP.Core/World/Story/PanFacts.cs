using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// The host checks whether everyone has crawled to their death
    /// (EpilogueSync.CheckPan).
    public readonly struct PanFacts
    {
        /// The pan is released (EpilogueSync._panGo).
        public bool Released { get; init; }

        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// A session is set and this game is its host.
        public bool IsHost { get; init; }

        /// The host's own player (Session.LocalId).
        public ulong Host { get; init; }

        /// The session's peers (Session.Peers); null counts as none.
        public IReadOnlyCollection<ulong> Peers { get; init; }
    }
}
