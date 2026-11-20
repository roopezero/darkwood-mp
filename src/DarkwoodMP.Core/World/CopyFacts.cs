namespace DarkwoodMP.World
{
    /// What TryBind looks at in an object that started recently.
    public readonly struct CopyFacts
    {
        public bool Alive { get; init; }

        /// How long ago it started.
        public Seconds Age { get; init; }

        /// It has the announced name.
        public bool SameName { get; init; }

        /// No id names it in every game (ObjRef.Unnamed).
        public bool Unnamed { get; init; }

        /// Tied a moment ago to an id from long before.
        public bool LateTied { get; init; }
    }
}
