namespace DarkwoodMP.World.Doors
{
    /// What this game knows when another game's DoorLeaf message (a
    /// leaf's signed angle) arrives (the plugin's DoorPush.OnLeaf).
    public readonly struct LeafHeardFacts
    {
        /// A world is loaded and running (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// The message came from the host.
        public bool FromHost { get; init; }

        /// The angle in the message.
        public float Angle { get; init; }

        /// The door it names, here: Exists, HasBody, Opened, Destroyed
        /// and Barricaded are read.
        public LeafDoorFacts Door { get; init; }

        /// Host: the pusher's stand-in and the leaf (Door.body.position),
        /// for Authority.Near. The rule sets the reach itself.
        public ReachFacts Pusher { get; init; }

        /// Host: how far the leaf may turn (LeafRules.MaxTurn).
        public float MaxTurn { get; init; }
    }
}
