namespace DarkwoodMP.World.KeptCharacters
{
    /// Which character the host gives a joiner, as CharacterLoad's first
    /// byte carries it.
    public enum CharacterKind : byte
    {
        /// Someone new: no items, full health, beside the host.
        New = 0,

        /// Their own kept character, where it was saved.
        Own = 1,

        /// A copy of the host's (the NewCharacters setting "copy").
        HostCopy = 2,

        /// Their own, from another chapter: beside the host.
        OwnBesideHost = 3,

        /// Their own, which died for good: they watch the others.
        Dead = 4,
    }
}
