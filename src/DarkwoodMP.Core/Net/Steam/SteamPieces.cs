using System;
using System.Collections.Generic;

namespace DarkwoodMP.Net.Steam
{
    /// A long reliable message as SteamPiece messages
    /// (SteamTransport.SendPieces and AddPiece). The first piece is the
    /// type byte, the whole message's length (u32, little-endian) and
    /// data; each later one the type byte and data.
    public static class SteamPieces
    {
        /// The bytes before the data in the first piece and in the others.
        public const int FirstHead = 5;
        public const int NextHead = 1;

        /// The message as pieces of SteamSendRules.PieceSize data each,
        /// the last one shorter. type is MsgType.SteamPiece. The shell
        /// sends every piece even after one fails.
        public static List<byte[]> Split(ArraySegment<byte> message, byte type)
        {
            var pieces = new List<byte[]>();
            int length = message.Count;
            for (int off = 0; off < length; off += SteamSendRules.PieceSize)
            {
                int n = Math.Min(SteamSendRules.PieceSize, length - off);
                int head = NextHead;
                if (off == 0)
                {
                    head = FirstHead;
                }

                var piece = new byte[head + n];
                piece[0] = type;
                if (off == 0)
                {
                    piece[1] = (byte)length;
                    piece[2] = (byte)(length >> 8);
                    piece[3] = (byte)(length >> 16);
                    piece[4] = (byte)(length >> 24);
                }

                Buffer.BlockCopy(
                    message.Array,
                    message.Offset + off,
                    piece,
                    head,
                    n);
                pieces.Add(piece);
            }

            return pieces;
        }

        /// The whole message's length that a first piece gives, or -1 for
        /// a piece shorter than its head.
        public static int LengthOf(ArraySegment<byte> piece)
        {
            if (piece.Count < FirstHead)
            {
                return -1;
            }

            var buf = piece.Array;
            int at = piece.Offset;
            return buf[at + 1]
                | buf[at + 2] << 8
                | buf[at + 3] << 16
                | buf[at + 4] << 24;
        }

        /// Whether a first piece's length can start a message: above zero
        /// and at most max (Protocol.MaxMessage).
        public static Verdict MayStart(int total, int max)
        {
            if (total <= 0 || total > max)
            {
                return Verdict.Refuse(
                    Refusal.BadMessageLength,
                    total.ToString());
            }

            return Verdict.Allow;
        }

        /// Whether n more bytes fit a message of size that has got bytes.
        public static Verdict MayAppend(int got, int n, int size)
        {
            if (got + n > size)
            {
                return Verdict.Refuse(Refusal.PiecesTooLong);
            }

            return Verdict.Allow;
        }
    }
}
