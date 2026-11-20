namespace DarkwoodMP.World.Dragging
{
    /// What the host knows about a client's ObjMove (DragSync.TakeMove).
    public readonly struct DragMoveFacts
    {
        /// The owner table has an entry for the thing.
        public bool Owned { get; init; }

        /// Who drags it, when Owned.
        public ulong Owner { get; init; }

        /// When the owner was last heard of, when Owned.
        public Seconds Heard { get; init; }

        /// Who sent the move.
        public ulong From { get; init; }

        /// The move is where the thing came to rest (the message's last).
        public bool Last { get; init; }

        /// The thing is in the host's world (ObjRef.Resolve of an Item).
        public bool Found { get; init; }

        /// The mover's stand-in is within the reach of where the thing is
        /// here (ReachRules.Near, ReachRules.Reach), when Found.
        public bool NearThing { get; init; }

        /// The same, of the place the move puts it, when Found.
        public bool NearPlace { get; init; }

        /// This mover has been sent this thing's place while someone else
        /// dragged it (DragSync._corrected has "ref/mover").
        public bool Corrected { get; init; }

        /// When, if Corrected.
        public Seconds CorrectedAt { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }
    }
}
