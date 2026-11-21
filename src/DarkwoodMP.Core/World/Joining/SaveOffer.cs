using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Joining
{
    /// The bytes of MsgType.SaveOffer (host to client): 32 bytes hash,
    /// u32 size, bigstr profile JSON, u8 n, n x (str name, u32 size),
    /// u8 packing, u32 unpacked size.
    public static class SaveOffer
    {
        /// The payload, after Begin(MsgType.SaveOffer).
        public static void Write(PacketWriter w, in SaveOfferBody body)
        {
            w.WriteBytes(body.Hash);
            w.W.Write(body.Size);
            w.WriteBigStr(body.ProfileJson);
            var files = body.Files;
            w.W.Write((byte)files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                w.WriteStr(files[i].Name);
                w.W.Write(files[i].Size);
            }

            w.W.Write(body.Packing);
            w.W.Write(body.Raw);
        }

        public static SaveOfferBody Read(PacketReader r)
        {
            var hash = r.R.ReadBytes(32);
            var size = r.R.ReadUInt32();
            var json = r.ReadBigStr();
            int n = r.R.ReadByte();
            var files = new List<OfferedFile>(n);
            for (int i = 0; i < n; i++)
            {
                var name = r.ReadStr();
                files.Add(new OfferedFile
                {
                    Name = name,
                    Size = r.R.ReadUInt32(),
                });
            }

            var packing = r.R.ReadByte();
            return new SaveOfferBody
            {
                Hash = hash,
                Size = size,
                ProfileJson = json,
                Files = files,
                Packing = packing,
                Raw = r.R.ReadUInt32(),
            };
        }
    }
}
