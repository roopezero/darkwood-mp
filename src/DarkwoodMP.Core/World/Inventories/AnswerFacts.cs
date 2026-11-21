namespace DarkwoodMP.World.Inventories
{
    /// What SlotShare.OnContents looks at when the host answers a client's
    /// ask for a container's contents.
    public readonly struct AnswerFacts
    {
        /// The container is here.
        public bool Found { get; init; }

        /// The answer is "": the host hasn't got it.
        public bool Empty { get; init; }

        /// We watch it slot by slot.
        public bool Watched { get; init; }

        /// We were waiting for this answer (it was in the waiting list,
        /// which the answer ends).
        public bool Waited { get; init; }

        /// Its window is open here.
        public bool Open { get; init; }
    }
}
