using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The epilogue's parts laid out as the host has them (PartLayout). The
    /// first part loops: its triggers move whole blocks of the street ahead of
    /// the player (teleportObjects). The host comes in first and its own
    /// player sets the first move off at once; a joiner that loads the part a
    /// few seconds later has the blocks where the scene left them, so the two
    /// players walk different streets (a forest in one game where the other
    /// has a clearing), and the joiner's triggers are then far from the
    /// host's. A joiner asks for the host's layout as each part starts (and
    /// again a little later), and moves its pieces to the host's spots.
    /// </summary>
    internal sealed partial class EpilogueSync
    {
        private static readonly float[] AskAfter = { 2f, 6f };

        private IEnumerator AskLayoutSoon()
        {
            foreach (float after in AskAfter)
            {
                yield return new WaitForSecondsRealtime(after);
                var s = S;
                string part = PartName();
                if (!Playing || s == null || !s.IsClient || part == null)
                {
                    yield break;
                }

                var w = s.Begin(MsgType.EpilogueStep);
                EpilogueStepMessage.WriteLayoutAsk(w, part);
                s.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: a joiner asks for the layout of its part; if we're in it too,
        /// our pieces' spots.
        /// </summary>
        private void SendLayout(ulong to, string part)
        {
            var s = S;
            Location loc = PartLocation();
            if (s == null || loc == null || PartName() != part)
            {
                return;
            }

            var w = s.Begin(MsgType.EpilogueStep);
            PartLayout.Write(w, part, PiecesOf(loc));
            s.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Joiner: the host's layout of a part; ours goes the same way if
        /// we're in that part.
        /// </summary>
        private void ApplyLayout(string part, PacketReader r)
        {
            List<PartPiece> hosts = PartLayout.ReadPieces(r);
            Location loc = PartLocation();
            if (loc == null || !PartLayout.Applies(Playing, PartName(), part))
            {
                return;
            }

            List<PartPiece> ours = PiecesOf(loc);
            Dictionary<int, Vec2> moves = PartLayout.Moves(ours, hosts);
            if (moves.Count == 0)
            {
                return;
            }

            var children = new List<Transform>();
            foreach (Transform t in loc.transform)
            {
                children.Add(t);
            }

            foreach (var move in moves)
            {
                Transform t = children[move.Key];
                Vector3 at = t.localPosition;
                t.localPosition = new Vector3(move.Value.X, at.y, move.Value.Z);
            }

            Plugin.Log.LogInfo(
                $"[epilogue] {part}: {moves.Count} piece(s) moved to where the "
                    + "host's stand");
        }

        /// <summary>
        /// The only code reading the part's pieces: each direct child of its
        /// location, in order.
        /// </summary>
        private static List<PartPiece> PiecesOf(Location loc)
        {
            var pieces = new List<PartPiece>();
            foreach (Transform t in loc.transform)
            {
                Vector3 at = t.localPosition;
                pieces.Add(new PartPiece
                {
                    Name = t.name,
                    At = new Vec2(at.x, at.z),
                });
            }

            return pieces;
        }

        /// The epilogue part we're dreaming (null: none).
        private static Location PartLocation()
        {
            var d = Singleton<Dreams>.Instance;
            if (d == null || !d.dreaming || d.switchingDream)
            {
                return null;
            }

            return d.dreamLocation;
        }

        private static string PartName()
        {
            var d = Singleton<Dreams>.Instance;
            if (PartLocation() == null || d.preset == null)
            {
                return null;
            }

            return d.preset.name;
        }
    }
}
