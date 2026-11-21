using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Infections
{
    /// The bytes of MsgType.InfectionCleared (either way): u16 n, then n
    /// times: ObjRef patch, f32 x, f32 z.
    public static class InfectionCleared
    {
        /// The payload, after Begin(MsgType.InfectionCleared).
        public static void Write(
            PacketWriter w, IReadOnlyList<ClearedPatch> patches)
        {
            w.W.Write((ushort)patches.Count);
            foreach (var p in patches)
            {
                p.Patch.Write(w);
                w.W.Write(p.At.X);
                w.W.Write(p.At.Z);
            }
        }

        public static List<ClearedPatch> Read(PacketReader r)
        {
            int n = r.R.ReadUInt16();
            var patches = new List<ClearedPatch>(n);
            for (int i = 0; i < n; i++)
            {
                ObjName patch = ObjName.Read(r);
                float x = r.R.ReadSingle();
                float z = r.R.ReadSingle();
                patches.Add(new ClearedPatch
                {
                    Patch = patch,
                    At = new Vec2(x, z),
                });
            }

            return patches;
        }
    }
}
