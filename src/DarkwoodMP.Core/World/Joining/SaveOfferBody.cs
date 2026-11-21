using System.Collections.Generic;

namespace DarkwoodMP.World.Joining
{
    /// One MsgType.SaveOffer's payload: the host's world.
    public readonly struct SaveOfferBody
    {
        /// 32 bytes, over the unpacked files (WorldFiles.Hash).
        public byte[] Hash { get; init; }

        /// The bytes to download.
        public uint Size { get; init; }

        /// The profile as JSON.
        public string ProfileJson { get; init; }

        /// The files, in order, with their unpacked sizes. At most 255.
        public IReadOnlyList<OfferedFile> Files { get; init; }

        /// 0: the files back to back; 1: Deflate of them.
        public byte Packing { get; init; }

        /// The files' sizes added up.
        public uint Raw { get; init; }
    }
}
