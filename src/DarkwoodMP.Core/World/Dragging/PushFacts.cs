namespace DarkwoodMP.World.Dragging
{
    /// One watched loose thing at a push check (DragSync.WatchPushes, an
    /// entry of _loose). Distances are 3D, from where it is known to be.
    public readonly struct PushFacts
    {
        /// Its body or its Item is gone.
        public bool Gone { get; init; }

        /// Its body is kinematic.
        public bool Kinematic { get; init; }

        /// Our player drags it (_dragging).
        public bool Dragged { get; init; }

        /// Our player let go of it and its end goes out again (_settling).
        public bool Settling { get; init; }

        /// It is shown following another game's move (_moving).
        public bool Shown { get; init; }

        /// How far it moved from where it is known to be.
        public float Moved { get; init; }

        /// Degrees it turned from there (Quaternion.Angle).
        public float Turned { get; init; }

        /// It was seen away from there before (Loose.Since >= 0).
        public bool Strayed { get; init; }

        /// When, if Strayed.
        public Seconds Since { get; init; }

        /// A client's own player was at it while it was away.
        public bool Touched { get; init; }

        /// This game is the host (Session.IsHost; while sharing a game is
        /// one or the other).
        public bool Host { get; init; }

        /// The host runs the area our player is in
        /// (Locations.HostRunsHere).
        public bool HostRunsHere { get; init; }

        /// It has a collider (Loose.Col).
        public bool Collider { get; init; }

        /// The squared distance from that collider's bounds to our player
        /// (Bounds.SqrDistance), when Collider.
        public float Reach { get; init; }

        /// Our player hit it (Loose.HitAt >= 0).
        public bool Hit { get; init; }

        /// When, if Hit.
        public Seconds HitAt { get; init; }

        /// Its body is at rest (PushRules.AtRest).
        public bool AtRest { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
