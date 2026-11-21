using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Npcs
{
    /// The bytes of MsgType.NpcPos (host to clients): u16 n, then n times
    /// (i32 save id, f32 x, f32 z).
    public static class NpcPosMessage
    {
        /// The payload, after Begin(MsgType.NpcPos): the places of the
        /// list from start, at most NpcPlaceRules.PerMessage of them.
        /// Returns how many it wrote.
        public static int Write(
            PacketWriter w,
            IReadOnlyList<NpcPlace> places,
            int start)
        {
            int n = NpcPlaceRules.BatchSize(places.Count, start);
            w.W.Write((ushort)n);
            for (int i = start; i < start + n; i++)
            {
                NpcPlace place = places[i];
                w.W.Write(place.Id);
                w.W.Write(place.At.X);
                w.W.Write(place.At.Z);
            }

            return n;
        }

        /// The places, in the order they came.
        public static List<NpcPlace> Read(PacketReader r)
        {
            int n = r.R.ReadUInt16();
            var places = new List<NpcPlace>(n);
            for (int i = 0; i < n; i++)
            {
                int id = r.R.ReadInt32();
                float x = r.R.ReadSingle();
                float z = r.R.ReadSingle();
                places.Add(new NpcPlace { Id = id, At = new Vec2(x, z) });
            }

            return places;
        }
    }
}
