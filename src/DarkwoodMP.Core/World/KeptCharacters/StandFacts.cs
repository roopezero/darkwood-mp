namespace DarkwoodMP.World.KeptCharacters
{
    /// What decides where our character stands once its record is applied
    /// (Characters.OnLoad).
    public readonly struct StandFacts
    {
        public CharacterKind Kind { get; init; }

        /// Prologue.Active.
        public bool Prologue { get; init; }

        /// Where the host stands, as CharacterLoad sent it.
        public Vec3 Spawn { get; init; }

        /// OnMap of our place after the record was applied.
        public bool OnMap { get; init; }
    }
}
