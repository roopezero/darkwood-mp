namespace DarkwoodMP.World.Throws
{
    /// What a game does with an ItemThrown (ThrowRules.Receive). The
    /// default does nothing.
    public readonly struct ThrowReceipt
    {
        /// Host: thrown from where the sender isn't. Logged ("[authority]
        /// {name} threw a {type} from where they aren't: not thrown"),
        /// nothing sent, nothing made.
        public bool Refused { get; init; }

        /// Host: relay it to everyone but the sender, with By as the
        /// thrower. Before the copy is made.
        public bool Relays { get; init; }

        /// Make the copy of the throw here.
        public bool Copies { get; init; }

        /// Who threw it, for the relay and the copy.
        public ulong By { get; init; }
    }
}
