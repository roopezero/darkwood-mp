namespace DarkwoodMP.World.Dragging
{
    /// What our game does with an ObjMove it applies.
    public enum ApplyAction : byte
    {
        /// Not here: a debug line only.
        NotHere = 0,

        /// Not here: HostIds.HostMoved is told where it is, for when our
        /// copy is tied, then the debug line.
        TellHostIds,

        /// Our player holds it: only its place is kept (_hostAt), for if we
        /// have to let go.
        KeepHostWord,

        /// Client, a last move: shown unless it is our own end passed back
        /// (EchoList.Take), which is skipped.
        CheckEcho,

        /// Shown moving (held kinematic and glided there).
        Show,
    }
}
