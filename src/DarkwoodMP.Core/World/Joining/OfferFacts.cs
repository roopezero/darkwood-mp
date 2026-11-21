using System.Collections.Generic;

namespace DarkwoodMP.World.Joining
{
    /// What a client checks in the host's SaveOffer
    /// (WorldShare.OnSaveOffer), in the order the bytes come.
    public readonly struct OfferFacts
    {
        /// The files, in order.
        public IReadOnlyList<OfferedFile> Files { get; init; }

        /// The packing (0: none, 1: Deflate), the unpacked size and the
        /// size sent.
        public byte Packing { get; init; }

        public uint Raw { get; init; }

        public uint Size { get; init; }

        /// The JSON reader's message when the profile couldn't be read;
        /// null when it could.
        public string ProfileError { get; init; }

        /// The profile read (it can read as null), and its id.
        public bool Profile { get; init; }

        public int ProfileId { get; init; }
    }
}
