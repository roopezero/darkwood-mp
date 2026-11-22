using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemySpawn (host -> client): u32 id, i32 save
    /// id, str prefab path, str kind, vec3 position, quat rotation, u8
    /// shadow armour (1 it wears it).
    public static class EnemySpawnMessage
    {
        /// The payload, after Begin(MsgType.EnemySpawn).
        public static void Write(PacketWriter w, in EnemySpawned e)
        {
            w.W.Write(e.Id);
            w.W.Write(e.SavedId);
            w.WriteStr(e.Prefab);
            w.WriteStr(e.Kind);
            w.Write(e.At);
            QuatBytes.Write(w, e.Rot);
            w.W.Write(Flag.Of(e.Armor));
        }

        public static EnemySpawned Read(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            int saved = r.R.ReadInt32();
            string prefab = r.ReadStr();
            string kind = r.ReadStr();
            Vec3 at = r.ReadVec3();
            Quat rot = QuatBytes.Read(r);
            bool armor = r.R.ReadByte() != 0;
            return new EnemySpawned
            {
                Id = id,
                SavedId = saved,
                Prefab = prefab,
                Kind = kind,
                At = at,
                Rot = rot,
                Armor = armor,
            };
        }
    }
}
