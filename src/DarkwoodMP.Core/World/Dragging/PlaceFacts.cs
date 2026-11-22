namespace DarkwoodMP.World.Dragging
{
    /// Client: our copy of a thing the host gave an id to has just been tied
    /// (DragSync.PutAtHost, from HostIds.Place).
    public readonly struct PlaceFacts
    {
        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// It is loose (PushRules.IsLoose).
        public bool Loose { get; init; }

        /// Its body is kinematic.
        public bool Kinematic { get; init; }

        /// Our player drags it.
        public bool Dragged { get; init; }

        /// Our player let go of it and its end goes out again.
        public bool Settling { get; init; }

        /// It is shown following another game's move.
        public bool Shown { get; init; }

        /// How far (3D) it is from where the host has it.
        public float Distance { get; init; }

        /// Someone else who drags it (DragRules.OtherOwner, 0 for nobody,
        /// and 0 when its name isn't valid).
        public ulong Other { get; init; }
    }
}
