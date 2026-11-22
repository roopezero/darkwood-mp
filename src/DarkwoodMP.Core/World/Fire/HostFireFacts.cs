namespace DarkwoodMP.World.Fire
{
    /// What a client knows about the thing the host's Burning names.
    public readonly struct HostFireFacts
    {
        /// The message's what byte, as sent.
        public FireKind Kind { get; init; }

        /// A thing of that kind was found by its ObjRef.
        public bool Found { get; init; }

        /// The message's on byte.
        public bool On { get; init; }

        /// The thing has a Burn here.
        public bool Burning { get; init; }
    }
}
