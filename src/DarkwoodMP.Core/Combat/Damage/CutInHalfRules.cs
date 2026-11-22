namespace DarkwoodMP.Combat.Damage
{
    /// The game's own cutMeInHalf destroys the upper half's Rigidbody but
    /// leaves it active, so Character.Update throws every frame of the
    /// Cut_half animation, with or without the mod.
    public static class CutInHalfRules
    {
        /// Whether the exception is dropped: a NullReferenceException of a
        /// character being cut in half that has no Rigidbody. Alone too.
        public static bool Drops(in CutFacts cut) =>
            cut.NullReference && cut.Character && cut.Cutting && !cut.Body;
    }
}
