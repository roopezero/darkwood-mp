using DarkwoodMP.Net;

namespace DarkwoodMP.World.KeptCharacters
{
    /// The bytes of MsgType.CharacterSave (client to host): bigstr player,
    /// bigstr inventory, bigstr hotbar, i32 chapter.
    public static class CharacterSave
    {
        /// The payload, after Begin(MsgType.CharacterSave).
        public static void Write(PacketWriter w, in CharacterSaveBody body)
        {
            w.WriteBigStr(body.Player);
            w.WriteBigStr(body.Inventory);
            w.WriteBigStr(body.Hotbar);
            w.W.Write(body.Chapter);
        }

        public static CharacterSaveBody Read(PacketReader r)
        {
            var player = r.ReadBigStr();
            var inventory = r.ReadBigStr();
            var hotbar = r.ReadBigStr();
            return new CharacterSaveBody
            {
                Player = player,
                Inventory = inventory,
                Hotbar = hotbar,
                Chapter = r.R.ReadInt32(),
            };
        }
    }
}
