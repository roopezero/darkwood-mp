using DarkwoodMP.Net;

namespace DarkwoodMP.World.KeptCharacters
{
    /// The bytes of MsgType.CharacterLoad (host to client): u8 kind,
    /// bigstr player, bigstr inventory, bigstr hotbar, vec3 where the host
    /// stands.
    public static class CharacterLoad
    {
        /// The payload, after Begin(MsgType.CharacterLoad).
        public static void Write(PacketWriter w, in CharacterLoadBody body)
        {
            w.W.Write((byte)body.Kind);
            w.WriteBigStr(body.Player);
            w.WriteBigStr(body.Inventory);
            w.WriteBigStr(body.Hotbar);
            w.Write(body.At);
        }

        public static CharacterLoadBody Read(PacketReader r)
        {
            var kind = (CharacterKind)r.R.ReadByte();
            var player = r.ReadBigStr();
            var inventory = r.ReadBigStr();
            var hotbar = r.ReadBigStr();
            return new CharacterLoadBody
            {
                Kind = kind,
                Player = player,
                Inventory = inventory,
                Hotbar = hotbar,
                At = r.ReadVec3(),
            };
        }
    }
}
