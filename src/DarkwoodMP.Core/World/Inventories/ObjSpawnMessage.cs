using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ObjSpawn (either way): i64 id, u8 flags (1
    /// placed by a player), str prefab, vec3 pos, quat rot (x y z w),
    /// bigstr contents, i32 experience.
    public static class ObjSpawnMessage
    {
        /// Flag: a player placed it.
        public const byte PlacedFlag = 1;

        /// The payload, after Begin(MsgType.ObjSpawn).
        public static void Write(PacketWriter w, in SpawnedThing thing)
        {
            w.W.Write(thing.Id);
            byte flags = 0;
            if (thing.Placed)
            {
                flags = PlacedFlag;
            }

            w.W.Write(flags);
            w.WriteStr(thing.Prefab);
            w.Write(thing.Pos);
            w.W.Write(thing.Rot.X);
            w.W.Write(thing.Rot.Y);
            w.W.Write(thing.Rot.Z);
            w.W.Write(thing.Rot.W);
            w.WriteBigStr(thing.Contents);
            w.W.Write(thing.Experience);
        }

        /// The thing as it came. Flags other than PlacedFlag are ignored.
        public static SpawnedThing Read(PacketReader r)
        {
            long id = r.R.ReadInt64();
            bool placed = (r.R.ReadByte() & PlacedFlag) != 0;
            string prefab = r.ReadStr();
            Vec3 pos = r.ReadVec3();
            var rot = new Quat(
                r.R.ReadSingle(),
                r.R.ReadSingle(),
                r.R.ReadSingle(),
                r.R.ReadSingle());
            string contents = r.ReadBigStr();
            int exp = r.R.ReadInt32();
            return new SpawnedThing
            {
                Id = id,
                Placed = placed,
                Prefab = prefab,
                Pos = pos,
                Rot = rot,
                Contents = contents,
                Experience = exp,
            };
        }
    }
}
