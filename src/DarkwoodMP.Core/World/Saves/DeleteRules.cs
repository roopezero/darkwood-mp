using System;
using IO = System.IO;

namespace DarkwoodMP.World.Saves
{
    /// The fence around every folder the mod deletes (WorldFiles today): a
    /// tree only strictly inside the mod's cache or the save redirect's
    /// folder, never a root, a relative path or the home folder, and a
    /// players' folder only at its exact place in a slot 1 to 99.
    public static class DeleteRules
    {
        public const int FirstSlot = 1;
        public const int LastSlot = 99;

        public const string PlayersFolder = "darkwoodmp_players";

        /// The checks every delete passes, first that applies wins
        /// (WorldFiles.NeverDelete).
        public static Verdict NeverDelete(in DeleteFacts facts)
        {
            var path = facts.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return Verdict.Refuse(Refusal.PathEmpty);
            }

            if (!IO.Path.IsPathRooted(path))
            {
                return Verdict.Refuse(Refusal.PathNotAbsolute);
            }

            string full;
            try
            {
                full = Full(path);
            }
            catch (Exception e)
            {
                return Verdict.Refuse(Refusal.PathInvalid, e.Message);
            }

            if (full.Length == 0
                || IO.Path.GetDirectoryName(full) == null
                || string.IsNullOrEmpty(IO.Path.GetFileName(full)))
            {
                return Verdict.Refuse(Refusal.FilesystemRoot);
            }

            if (HoldsHome(facts.Home, full, facts.IgnoreCase)
                || HoldsHome(facts.UserProfile, full, facts.IgnoreCase))
            {
                return Verdict.Refuse(Refusal.HomeFolder);
            }

            return Verdict.Allow;
        }

        /// May this tree be deleted (WorldFiles.MayDelete): the checks
        /// above, then strictly inside the cache, or inside the redirect's
        /// folder when that folder passes the checks itself.
        public static Verdict MayDelete(in DeleteFacts facts)
        {
            var never = NeverDelete(facts);
            if (never.Refused)
            {
                return never;
            }

            var full = Full(facts.Path);
            if (StrictlyUnder(full, facts.CacheRoot, facts.IgnoreCase))
            {
                return Verdict.Allow;
            }

            if (facts.Redirect != null
                && !NeverDelete(facts with { Path = facts.Redirect }).Refused
                && StrictlyUnder(full, facts.Redirect, facts.IgnoreCase))
            {
                return Verdict.Allow;
            }

            return Verdict.Refuse(Refusal.OutsideModFolders);
        }

        /// May a slot's players' folder be deleted
        /// (WorldFiles.DeletePlayersFolder): the slot first, then the save
        /// folder's checks (facts.Path), then the exact place.
        public static Verdict MayDeletePlayers(in DeleteFacts facts)
        {
            if (facts.Slot < FirstSlot || facts.Slot > LastSlot)
            {
                return Verdict.Refuse(
                    Refusal.NotASaveSlot,
                    facts.Slot.ToString());
            }

            var never = NeverDelete(facts);
            if (never.Refused)
            {
                return never;
            }

            var sep = IO.Path.DirectorySeparatorChar;
            var full = Full(PlayersOf(facts.Path, facts.Slot));
            var expected = Full(facts.Path) + sep + "prof" + facts.Slot
                + sep + PlayersFolder;
            if (!string.Equals(full, expected, Case(facts.IgnoreCase)))
            {
                return Verdict.Refuse(Refusal.NotPlayersFolder);
            }

            return Verdict.Allow;
        }

        /// The folder where the host keeps the characters of a slot's
        /// joiners: save folder, prof<slot>, darkwoodmp_players.
        public static string PlayersOf(string saves, int slot)
        {
            var prof = IO.Path.Combine(saves, "prof" + slot);
            return IO.Path.Combine(prof, PlayersFolder);
        }

        /// The full path without a separator at the end (WorldFiles.Full).
        public static string Full(string path) =>
            IO.Path.GetFullPath(path).TrimEnd(
                IO.Path.DirectorySeparatorChar,
                IO.Path.AltDirectorySeparatorChar);

        /// Both name the same folder; false when either is null, and a
        /// plain comparison when a full path can't be made.
        public static bool SamePath(string a, string b, bool ignoreCase)
        {
            if (a == null || b == null)
            {
                return false;
            }

            try
            {
                return string.Equals(Full(a), Full(b), Case(ignoreCase));
            }
            catch
            {
                return a == b;
            }
        }

        /// The path is the folder or inside it; false when either is null
        /// or a full path can't be made.
        public static bool IsUnder(string path, string dir, bool ignoreCase)
        {
            if (path == null || dir == null)
            {
                return false;
            }

            try
            {
                var p = Full(path);
                var d = Full(dir);
                var c = Case(ignoreCase);
                return string.Equals(p, d, c)
                    || p.StartsWith(d + IO.Path.DirectorySeparatorChar, c);
            }
            catch
            {
                return false;
            }
        }

        static bool StrictlyUnder(string full, string dir, bool ignoreCase) =>
            !SamePath(full, dir, ignoreCase) && IsUnder(full, dir, ignoreCase);

        static bool HoldsHome(string home, string full, bool ignoreCase) =>
            !string.IsNullOrEmpty(home)
                && IO.Path.IsPathRooted(home)
                && IsUnder(home, full, ignoreCase);

        static StringComparison Case(bool ignoreCase)
        {
            if (ignoreCase)
            {
                return StringComparison.OrdinalIgnoreCase;
            }

            return StringComparison.Ordinal;
        }
    }
}
