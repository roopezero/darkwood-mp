using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The layout of an epilogue part, as the host has it. Some parts move
    /// their own pieces as a player walks through them (the first part's
    /// looping street and stairs: teleportObjects steps that put a block of
    /// buildings ahead of the player), so a joiner that comes in after the
    /// host's first moves has the pieces where the scene left them, and the
    /// players walk through different streets. The joiner asks for the host's
    /// layout once its part has started and puts its pieces where the host's
    /// stand.
    ///
    /// Bytes (after EpilogueStep's u8 6 and str the part's preset name):
    /// u16 n, then n times: str the piece's name, f32 x, f32 z (in the
    /// location, before its turn).
    /// </summary>
    public static class PartLayout
    {
        /// At most this many pieces are sent.
        public const int MaxPieces = 512;

        /// Pieces closer than this to the host's spot aren't moved.
        public const float Near = 1f;

        public static void Write(
            PacketWriter w, string part, IReadOnlyList<PartPiece> pieces)
        {
            w.W.Write((byte)EpilogueStep.Layout);
            w.WriteStr(part);
            int n = pieces.Count;
            if (n > MaxPieces)
            {
                n = MaxPieces;
            }

            w.W.Write((ushort)n);
            for (int i = 0; i < n; i++)
            {
                w.WriteStr(pieces[i].Name);
                w.W.Write(pieces[i].At.X);
                w.W.Write(pieces[i].At.Z);
            }
        }

        /// The pieces, after EpilogueStepMessage.Read of a Layout (whose
        /// value is the part's name).
        public static List<PartPiece> ReadPieces(PacketReader r)
        {
            int n = r.R.ReadUInt16();
            var pieces = new List<PartPiece>(n);
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadStr();
                float x = r.R.ReadSingle();
                float z = r.R.ReadSingle();
                pieces.Add(new PartPiece { Name = name, At = new Vec2(x, z) });
            }

            return pieces;
        }

        /// Where each of our pieces goes: the host's piece of the same name
        /// (the k-th of a name for our k-th), only for pieces that stand
        /// elsewhere. The result maps our index to the host's spot.
        public static Dictionary<int, Vec2> Moves(
            IReadOnlyList<PartPiece> ours, IReadOnlyList<PartPiece> hosts)
        {
            var queue = new Dictionary<string, Queue<Vec2>>();
            foreach (var piece in hosts)
            {
                if (!queue.TryGetValue(piece.Name, out var spots))
                {
                    spots = new Queue<Vec2>();
                    queue[piece.Name] = spots;
                }

                spots.Enqueue(piece.At);
            }

            var moves = new Dictionary<int, Vec2>();
            for (int i = 0; i < ours.Count; i++)
            {
                if (!queue.TryGetValue(ours[i].Name, out var spots)
                    || spots.Count == 0)
                {
                    continue;
                }

                Vec2 to = spots.Dequeue();
                if (Vec2.Distance(ours[i].At, to) > Near)
                {
                    moves[i] = to;
                }
            }

            return moves;
        }

        /// Whether a layout from the host is of the part we are in.
        public static bool Applies(bool playing, string ours, string hosts)
        {
            return playing && !string.IsNullOrEmpty(ours) && ours == hosts;
        }
    }
}
