namespace DarkwoodMP.Combat.Motion
{
    /// Where a host's enemy is drawn this frame. Its rotation is From,
    /// or, when Blends, Quaternion.Slerp(From, To, Blend), as the shell's
    /// EnemyMotion.Where turns it today.
    public readonly struct MotionPose
    {
        public Vec3 Pos { get; init; }

        /// The rotation, or the one turned from when Blends.
        public Facing From { get; init; }

        /// The rotation turned to when Blends.
        public Facing To { get; init; }

        /// How far from From to To, 0 to 1, when Blends.
        public float Blend { get; init; }

        /// Drawn between two states: turned between their rotations.
        public bool Blends { get; init; }
    }
}
