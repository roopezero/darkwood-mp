namespace DarkwoodMP.World.Fire
{
    /// What the host knows about the thing a client's Burning names.
    public readonly struct IgniteFacts
    {
        /// The message's on byte: a client may only light things.
        public bool On { get; init; }

        /// The message's what byte, as sent: any value, 5 and more too.
        public FireKind Kind { get; init; }

        /// A thing of that kind was found by its ObjRef.
        public bool Found { get; init; }

        /// Item.destructible (items only).
        public bool Destructible { get; init; }

        /// Item.destroyed or Door.destroyed (items and doors only).
        public bool Destroyed { get; init; }

        /// It has a Burn already.
        public bool Burning { get; init; }

        /// The burn time the client sent.
        public float Time { get; init; }

        /// Its name, for the refusals' words.
        public string Name { get; init; }

        /// The requester and the thing; Handling says the message came
        /// inside a Request. The rule sets the reach and the name.
        public ReachFacts Reach { get; init; }
    }
}
