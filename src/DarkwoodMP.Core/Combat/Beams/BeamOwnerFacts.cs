namespace DarkwoodMP.Combat.Beams
{
    /// A stand-in whose player's flashlight may be on a thing
    /// (ShadowArmorJoinerLightPatch.BeamOn), before the beam's shape.
    public readonly struct BeamOwnerFacts
    {
        /// The stand-in isn't null and is alive.
        public bool StandInAlive { get; init; }

        /// Its player is among PlayerSync.Remotes and their puppet has a
        /// root.
        public bool Root { get; init; }

        /// RemotePlayer.Last isn't null.
        public bool HasSnapshot { get; init; }

        /// The snapshot's Flashlight.
        public bool Flashlight { get; init; }

        /// The snapshot's Alive.
        public bool Alive { get; init; }

        /// The snapshot's Spectating.
        public bool Spectating { get; init; }
    }
}
