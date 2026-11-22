namespace DarkwoodMP.Combat.Damage
{
    /// What Character.Update threw (CharacterUpdateCutInHalf.Finalizer).
    public readonly struct CutFacts
    {
        /// The exception is a NullReferenceException.
        public bool NullReference { get; init; }

        /// The character isn't null.
        public bool Character { get; init; }

        /// cuttingInHalf.
        public bool Cutting { get; init; }

        /// It has a Rigidbody.
        public bool Body { get; init; }
    }
}
