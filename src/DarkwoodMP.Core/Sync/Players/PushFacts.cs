using System.Collections.Generic;

namespace DarkwoodMP.Sync.Players
{
    /// Our player and the puppets around it, for one frame's easing
    /// (PlayerSeparation.Update).
    public readonly struct PushFacts
    {
        /// Our rigidbody's position on the ground plane.
        public Vec2 Ours { get; init; }

        /// Session.LocalId.
        public ulong LocalId { get; init; }

        /// Our player's body radius (SeparationRules.Radius).
        public float Radius { get; init; }

        /// Time.deltaTime, scaled.
        public Seconds Delta { get; init; }

        /// Every puppet, in the order of PlayerSync.Remotes.
        public IReadOnlyList<PuppetBody> Puppets { get; init; }
    }
}
