namespace DarkwoodMP.Combat.Joiners
{
    /// Whether a joiner's stand-in may see a thing (JoinerSight.Sees).
    /// The host's player's field of view and light stand in for every
    /// joiner's.
    public readonly struct ViewFacts
    {
        /// The stand-in isn't null and is alive.
        public bool StandInAlive { get; init; }

        /// The thing's transform isn't null.
        public bool Target { get; init; }

        /// From the stand-in to the thing (Core.trueDistance).
        public float Distance { get; init; }

        /// Player.Instance isn't null.
        public bool HasHost { get; init; }

        /// The host's player's currentFOV, in degrees.
        public float HostFov { get; init; }

        /// The host has a player with an FOVDot.
        public bool HasLight { get; init; }

        /// FOVDot.LightRadius: inside it, the angle doesn't matter.
        public float LightRadius { get; init; }

        /// Vector3.Angle between the stand-in's up (where it faces) and the
        /// way to the thing, in 3D, in degrees.
        public float Angle { get; init; }
    }
}
