namespace DarkwoodMP.Net.Steam
{
    /// What one SteamPiece gave: the whole message once its last piece is
    /// in, or why the piece was dropped. Both empty while more must come.
    public readonly struct PieceAnswer
    {
        /// The whole message, or null.
        public byte[] Whole { get; init; }

        /// Why the piece was dropped (logged "Dropped a piece from {from}:"
        /// for BadMessageLength, "Dropped a long message from {from}:" for
        /// PiecesTooLong), or Allow.
        public Verdict Why { get; init; }
    }
}
