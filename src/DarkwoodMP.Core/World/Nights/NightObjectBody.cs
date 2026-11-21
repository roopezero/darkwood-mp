namespace DarkwoodMP.World.Nights
{
    /// A night object, as NightObjectMessage carries it. A Gone one has only
    /// its Kind and Id.
    public readonly struct NightObjectBody
    {
        /// NightObjectRules.Made, Gone or MadeBefore.
        public byte Kind { get; init; }

        /// The id the host gave it (ObjRef.Spawned).
        public long Id { get; init; }

        /// The night scenario's name ("" for none).
        public string Scenario { get; init; }

        /// The night event's name.
        public string Event { get; init; }

        /// Which of the event's GameEvents (a byte on the wire).
        public int Group { get; init; }

        /// Which step of those GameEvents (a byte on the wire).
        public int Step { get; init; }

        public Vec3 Pos { get; init; }

        public Quat Rot { get; init; }

        /// What it was made under; Kind None for nothing.
        public ObjName Parent { get; init; }

        /// Its trap: NightObjectRules.Armed, Unarmed or NoTrap.
        public byte Armed { get; init; }
    }
}
