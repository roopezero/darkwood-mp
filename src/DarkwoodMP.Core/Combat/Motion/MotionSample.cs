namespace DarkwoodMP.Combat.Motion
{
    /// One state of a host's enemy: when the host sent it (its clock),
    /// where the enemy was and which way it faced.
    public readonly struct MotionSample
    {
        public readonly Seconds Sent;
        public readonly Vec3 Pos;
        public readonly Facing Rot;

        public MotionSample(Seconds sent, Vec3 pos, Facing rot)
        {
            Sent = sent;
            Pos = pos;
            Rot = rot;
        }
    }
}
