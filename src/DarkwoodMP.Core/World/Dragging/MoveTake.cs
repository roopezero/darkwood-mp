namespace DarkwoodMP.World.Dragging
{
    /// What the host does with a client's ObjMove, in this order of the
    /// flags. Taken: the move is relayed and applied (DragSync.OnMove);
    /// otherwise it goes no further.
    public readonly struct MoveTake
    {
        /// Someone else's drag of it went stale: freed first (Free,
        /// "stopped sending": the place to everyone, DragOwner(0, let go)),
        /// and the move goes on as one of a thing nobody drags.
        public bool FreeStale { get; init; }

        /// Someone else drags it: the mover is sent the host's place
        /// (SendHere), its time noted in DragSync._corrected, and a log line.
        public bool Correct { get; init; }

        /// Out of reach: logged and the host's place sent to the mover as
        /// an ObjMove, last.
        public bool PutBack { get; init; }

        /// The mover's own drag is taken from them: their entry removed and
        /// DragOwner(0, let go) to everyone.
        public bool Disown { get; init; }

        /// The mover's own drag: heard now.
        public bool Refresh { get; init; }

        /// The end of the mover's own drag: their entry removed and
        /// DragOwner(0, stay) to everyone.
        public bool EndDrag { get; init; }

        /// An unclaimed slide from too far: dropped, with a debug line.
        public bool Dropped { get; init; }

        /// The move is taken.
        public bool Taken { get; init; }
    }
}
