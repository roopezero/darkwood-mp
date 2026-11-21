using System.Security.Cryptography;
using System.Text;

namespace DarkwoodMP.World.Progress
{
    /// A skill reset at the stove: in the morning, once a day for each
    /// player, while someone shares the progression (Progression.TakeReset,
    /// OnSkillReset, CanResetHere).
    public static class SkillResetRules
    {
        /// Who the host's own player is in the record's reset days.
        public const string HostWho = "host";

        /// How many hex characters of the key's SHA-256 name a client in
        /// the record. Changing it would reset players' reset days.
        public const int WhoLength = 32;

        /// Who a client is in the record: the first 32 lower-case hex
        /// characters of the SHA-256 of its player key in UTF-8. A null key
        /// counts as "".
        public static string Who(string key)
        {
            if (key == null)
            {
                key = "";
            }

            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            }

            var hex = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                hex.Append(b.ToString("x2"));
            }

            return hex.ToString().Substring(0, WhoLength);
        }

        /// Whether the record lookup may start a record alone (HostSaved's
        /// alone): only for our own player's reset.
        public static bool Alone(string who)
        {
            return who == HostWho;
        }

        /// The first check: only in the player's morning, before they leave
        /// their hideout. It comes before the record is looked at, so with
        /// nobody sharing, a client is told NotMorning first.
        public static Verdict Morning(bool morning)
        {
            if (!morning)
            {
                return Verdict.Refuse(Refusal.NotMorning);
            }

            return Verdict.Allow;
        }

        /// The second check, once Morning allows it. Allowed: the shell
        /// records Today as this player's reset day.
        public static Verdict Today(in ResetFacts reset)
        {
            if (!reset.HasRecord)
            {
                return Verdict.Refuse(Refusal.NoSharedProgression);
            }

            if (reset.HasDay && reset.Day == reset.Today)
            {
                return Verdict.Refuse(Refusal.AlreadyReset);
            }

            return Verdict.Allow;
        }

        /// Whether the host takes in a client's SkillReset: only as the
        /// host, and only from a player with a key. Otherwise the handler
        /// returns without an answer, and the client resets with no day
        /// recorded.
        public static bool Handles(bool isHost, bool hasKey)
        {
            return isHost && hasKey;
        }

        /// Whether our player can reset here: at the stove, its menu open
        /// and still, no level being given, and the game's skill choice
        /// closed.
        public static bool CanResetHere(in StoveFacts stove)
        {
            return stove.HasPlayer
                && stove.InLevelingMenu
                && stove.HasMenu
                && stove.OpenedAndTweened
                && !stove.WantToLevel
                && !stove.SkillsMenuOpen;
        }

        /// Whether a client asks the host now (ResetHere): not while an
        /// answer is waiting.
        public static bool Asks(bool waiting)
        {
            return !waiting;
        }
    }
}
