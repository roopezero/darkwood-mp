using System.Collections.Generic;

namespace DarkwoodMP.World.Joining
{
    /// The checks on the host's world as it comes to a client, and on the
    /// pieces the host serves (WorldShare's download). A refusal ends the
    /// download: "Couldn't get the host's world: {text}".
    public static class OfferRules
    {
        /// A piece of the world: what a client asks for, and the most the
        /// host sends at once.
        public const int PieceSize = 256 << 10;

        /// Pieces a client asks for before the first comes.
        public const int PiecesInFlight = 4;

        /// The largest world, packed or not (WorldFiles.MaxSize).
        public const long MaxSize = 128L << 20;

        /// The highest packing a client unpacks (1: Deflate).
        public const byte MaxPacking = 1;

        public const int FirstSlot = 1;
        public const int LastSlot = 99;

        /// No piece for this long is a stall; more than MaxStalls in a row
        /// ends the download.
        public static readonly Seconds StallSecs = new Seconds(10f);
        public const int MaxStalls = 3;

        /// The files a world may have (WorldFiles.Names), in their order.
        public static readonly IReadOnlyList<string> Names =
            new[] { "sav.dat", "savs.dat", "savch.dat" };

        /// The checks on an offer, first that applies: each file in order
        /// (one the world can't have, or one twice), the packing, the
        /// sizes, the profile's JSON, the profile.
        public static Verdict Check(in OfferFacts facts)
        {
            long sum = 0;
            var files = facts.Files;
            var count = 0;
            if (files != null)
            {
                count = files.Count;
            }

            for (int i = 0; i < count; i++)
            {
                var name = files[i].Name;
                if (!Known(name) || NamedBefore(files, i))
                {
                    return Verdict.Refuse(Refusal.UnexpectedFile, name);
                }

                sum += files[i].Size;
            }

            if (facts.Packing > MaxPacking)
            {
                return Verdict.Refuse(
                    Refusal.UnknownPacking,
                    facts.Packing.ToString());
            }

            if (BadSize(facts, sum))
            {
                return Verdict.Refuse(Refusal.BadWorldSize);
            }

            if (facts.ProfileError != null)
            {
                return Verdict.Refuse(
                    Refusal.UnreadableProfile,
                    facts.ProfileError);
            }

            if (!facts.Profile
                || facts.ProfileId < FirstSlot
                || facts.ProfileId > LastSlot)
            {
                return Verdict.Refuse(Refusal.InvalidProfile);
            }

            return Verdict.Allow;
        }

        /// The checks on a piece of the world being downloaded: empty, off
        /// a piece's boundary or past the end. A shorter piece that isn't
        /// the last passes, and its missing bytes are never asked again.
        public static Verdict CheckPiece(in PieceFacts facts)
        {
            if (facts.Length <= 0
                || facts.Offset % PieceSize != 0
                || facts.Offset + (long)facts.Length > facts.Total)
            {
                return Verdict.Refuse(Refusal.BadPiece);
            }

            return Verdict.Allow;
        }

        /// How many bytes the host sends for a NeedSave (OnNeedSave): what
        /// was asked, at most PieceSize, and at most what's left; -1 for an
        /// offset at or past the end, which gets no answer.
        public static int Serves(uint offset, uint asked, int size)
        {
            if (offset >= size)
            {
                return -1;
            }

            uint length = asked;
            if (length > PieceSize)
            {
                length = PieceSize;
            }

            if (length > size - offset)
            {
                return (int)(size - offset);
            }

            return (int)length;
        }

        /// The number of pieces of a world of this size.
        public static int Pieces(uint size) =>
            (int)((size + PieceSize - 1) / PieceSize);

        /// A stall (UpdateDownload): StallSecs or more since the last piece
        /// (or the last stall).
        public static bool Stalled(Seconds last, Seconds now) =>
            !(now - last < StallSecs);

        /// After a stall, the download goes on while it has had at most
        /// MaxStalls in a row; the detail is in KiB.
        public static Verdict AfterStall(int stalls, long got, long total)
        {
            if (stalls > MaxStalls)
            {
                return Verdict.Refuse(
                    Refusal.HostStoppedSending,
                    Refusals.Detail(
                        (got / 1024).ToString(),
                        (total / 1024).ToString()));
            }

            return Verdict.Allow;
        }

        static bool BadSize(in OfferFacts facts, long sum) =>
            sum != facts.Raw
                || facts.Raw == 0
                || facts.Raw > MaxSize
                || facts.Size == 0
                || facts.Size > MaxSize
                || (facts.Packing == 0 && facts.Size != facts.Raw);

        static bool Known(string name)
        {
            for (int i = 0; i < Names.Count; i++)
            {
                if (Names[i] == name)
                {
                    return true;
                }
            }

            return false;
        }

        static bool NamedBefore(IReadOnlyList<OfferedFile> files, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (files[i].Name == files[index].Name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
