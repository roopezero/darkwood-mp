using System;
using System.Collections.Generic;

namespace DarkwoodMP.Net.Steam
{
    /// The long messages coming in as SteamPiece messages, per sender and
    /// channel (SteamTransport._joining, AddPiece, Append). Senders are
    /// CSteamID.m_SteamID.
    public sealed class PieceJoiner
    {
        sealed class Joining
        {
            public byte[] Data;
            public int Got;
        }

        readonly Dictionary<(ulong, int), Joining> _joining =
            new Dictionary<(ulong, int), Joining>();

        readonly int _max;

        /// max: the longest message taken (Protocol.MaxMessage).
        public PieceJoiner(int max)
        {
            _max = max;
        }

        /// How many messages are half joined.
        public int Count => _joining.Count;

        /// Adds a piece from one sender on one channel. The first piece
        /// starts a message; a half-joined one never times out, and a piece
        /// that fails to arrive leaves it short, so the next message's
        /// first piece is taken as more of it.
        public PieceAnswer Add(
            ulong from,
            int channel,
            ArraySegment<byte> piece)
        {
            var key = (from, channel);
            if (!_joining.TryGetValue(key, out var join))
            {
                int total = SteamPieces.LengthOf(piece);
                var why = SteamPieces.MayStart(total, _max);
                if (why.Refused)
                {
                    return new PieceAnswer { Why = why };
                }

                join = new Joining { Data = new byte[total] };
                _joining[key] = join;
                return Append(key, join, After(piece, SteamPieces.FirstHead));
            }

            return Append(key, join, After(piece, SteamPieces.NextHead));
        }

        /// Forgets a sender's half-joined message on one channel
        /// (CloseSession forgets channels 0 and 1).
        public void Forget(ulong from, int channel)
        {
            _joining.Remove((from, channel));
        }

        /// Forgets everything (we left the lobby).
        public void Clear()
        {
            _joining.Clear();
        }

        static ArraySegment<byte> After(ArraySegment<byte> piece, int head)
        {
            return new ArraySegment<byte>(
                piece.Array,
                piece.Offset + head,
                piece.Count - head);
        }

        /// Adds the data after a piece's head (data) to a message.
        PieceAnswer Append(
            (ulong, int) key,
            Joining join,
            ArraySegment<byte> data)
        {
            int n = data.Count;
            var why = SteamPieces.MayAppend(join.Got, n, join.Data.Length);
            if (why.Refused)
            {
                _joining.Remove(key);
                return new PieceAnswer { Why = why };
            }

            Buffer.BlockCopy(
                data.Array,
                data.Offset,
                join.Data,
                join.Got,
                n);
            join.Got += n;
            if (join.Got < join.Data.Length)
            {
                return default;
            }

            _joining.Remove(key);
            return new PieceAnswer { Whole = join.Data };
        }
    }
}
