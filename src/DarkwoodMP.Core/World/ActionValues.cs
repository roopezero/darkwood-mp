namespace DarkwoodMP.World
{
    /// The values of a WorldAction after its ObjRef (the plugin's
    /// WorldActions.WriteAction): what each means is the action's to say
    /// (a door's open: where the opener stood, a hit: its damage and
    /// kind).
    public readonly struct ActionValues
    {
        /// f32 a.
        public float X { get; init; }

        /// f32 b.
        public float Y { get; init; }

        /// Flags 1.
        public bool F1 { get; init; }

        /// Flags 2.
        public bool F2 { get; init; }

        /// str s.
        public string Str { get; init; }
    }
}
