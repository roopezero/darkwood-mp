using System.Collections.Generic;

namespace DarkwoodMP.Presence
{
    /// What the player's friends and the server browser are told about our
    /// game: Plugin.PresenceText, UpdatePresence's limits, WorldLabel,
    /// BiomeName, SetSteamPresence's player count, SteamBackend.SetPresence's
    /// group keys, and the timers in Plugin.Update.
    public static class PresenceRules
    {
        /// Between the presence's parts.
        public const string Separator = " · ";

        /// The lowest and highest chapter the presence names (the world
        /// label names any above zero).
        public const int FirstChapter = 1;
        public const int LastChapter = 10;

        /// Steam's limit for a rich presence value, in characters.
        public const int MaxLength = 256;

        /// How often a host works out its server browser label, and the
        /// presence is worked out, in unscaled seconds.
        public const float LabelEvery = 2f;
        public const float PresenceEvery = 5f;

        /// "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest ·
        /// Hosting, 3/5 players", or "DarkwoodMP 0.22.0 · In the main menu".
        /// The chapter is clamped to FirstChapter..LastChapter; without one
        /// it's "Day 5". An empty biome is left out. A client whose host's
        /// name is unknown or empty is "In co-op".
        public static string Text(in PresenceFacts game)
        {
            var parts = new List<string> { Fallback(game.Version) };
            if (!game.InWorld)
            {
                parts.Add("In the main menu");
                return string.Join(Separator, parts);
            }

            if (game.Chapter > 0)
            {
                int chapter = Clamp(game.Chapter, FirstChapter, LastChapter);
                parts.Add(
                    "Chapter " + chapter.ToString() + ", day "
                        + game.Day.ToString());
            }
            else
            {
                parts.Add("Day " + game.Day.ToString());
            }

            if (!string.IsNullOrEmpty(game.Biome))
            {
                parts.Add(game.Biome);
            }

            if (!game.InSession)
            {
                parts.Add("Playing alone");
                return string.Join(Separator, parts);
            }

            string count = game.Players.ToString() + "/"
                + game.MaxPlayers.ToString() + " players";
            if (game.IsHost)
            {
                parts.Add("Hosting, " + count);
            }
            else if (!string.IsNullOrEmpty(game.HostName))
            {
                parts.Add("In " + game.HostName + "'s game, " + count);
            }
            else
            {
                parts.Add("In co-op, " + count);
            }

            return string.Join(Separator, parts);
        }

        /// The presence when working it out threw: the version alone.
        public static string Fallback(string version) =>
            "DarkwoodMP " + version;

        /// The text cut to Steam's MaxLength.
        public static string Cut(string text)
        {
            if (text.Length > MaxLength)
            {
                return text.Substring(0, MaxLength);
            }

            return text;
        }

        /// The players Steam is told of: the session's count, 1 alone.
        public static int SteamPlayers(bool inSession, int players)
        {
            if (inSession)
            {
                return players;
            }

            return 1;
        }

        /// The "steam_player_group" value: the lobby's id in a lobby, else
        /// empty (which removes the key).
        public static string Group(bool inLobby, ulong lobby)
        {
            if (inLobby)
            {
                return lobby.ToString();
            }

            return "";
        }

        /// The "steam_player_group_size" value: the players in a lobby,
        /// else empty (which removes the key).
        public static string GroupSize(bool inLobby, int players)
        {
            if (inLobby)
            {
                return players.ToString();
            }

            return "";
        }

        /// The server browser's World column for this host: "Day 5,
        /// chapter 2", "Day 5" without a chapter, empty outside a world.
        /// The chapter isn't clamped here.
        public static string WorldLabel(bool inWorld, int day, int chapter)
        {
            if (!inWorld)
            {
                return "";
            }

            if (chapter > 0)
            {
                return "Day " + day.ToString() + ", chapter "
                    + chapter.ToString();
            }

            return "Day " + day.ToString();
        }

        /// Whether the player is somewhere with a biome to name: a player,
        /// not in a separate location, on a biome that isn't Empty.
        public static bool InBiome(in WhereFacts where)
        {
            if (!where.HasPlayer || where.Outside)
            {
                return false;
            }

            return where.HasBiome && where.Kind != BiomeKind.Empty;
        }

        /// The key of the biome's name in the game's "UI" sheet: "biome_"
        /// and the game's name of its type. Dense forest is forest on the
        /// game's map. A value the game's enum lacks gives its number, as
        /// the game's ToString() would.
        public static string BiomeKey(BiomeKind kind)
        {
            switch (kind)
            {
                case BiomeKind.Forest:
                case BiomeKind.ForestDense:
                    return "biome_forest";
                case BiomeKind.ForestMutated:
                    return "biome_forest_mutated";
                case BiomeKind.Swamp:
                    return "biome_swamp";
                case BiomeKind.Meadow:
                    return "biome_meadow";
                case BiomeKind.Empty:
                    return "biome_empty";
                default:
                    return "biome_" + ((int)kind).ToString();
            }
        }

        /// The biome's name: the sheet's (Language.Get(BiomeKey, "UI"),
        /// empty when reading it threw) unless it's empty or the key itself,
        /// else an English name. The mutated forest has none, so it's empty
        /// when the sheet fails.
        public static string BiomeName(BiomeKind kind, string sheet)
        {
            if (!string.IsNullOrEmpty(sheet) && sheet != BiomeKey(kind))
            {
                return sheet;
            }

            switch (kind)
            {
                case BiomeKind.Meadow:
                    return "Dry Meadow";
                case BiomeKind.Forest:
                case BiomeKind.ForestDense:
                    return "Silent Forest";
                case BiomeKind.Swamp:
                    return "Swamp";
                default:
                    return "";
            }
        }

        /// Host: whether to work out the server browser label this frame.
        public static bool LabelDue(bool isHost, Seconds now, Seconds next)
        {
            return isHost && now.Value >= next.Value;
        }

        /// When the label is due again after one at now.
        public static Seconds NextLabel(Seconds now) =>
            new Seconds(now.Value + LabelEvery);

        /// Whether to work out the presence this frame: with Steam up, or
        /// to log it for a test, once it's due.
        public static bool PresenceDue(in PresenceTimerFacts timer)
        {
            if (!timer.SteamUp && !timer.Logged)
            {
                return false;
            }

            return timer.Now.Value >= timer.Next.Value;
        }

        /// When the presence is due again after one at now.
        public static Seconds NextPresence(Seconds now) =>
            new Seconds(now.Value + PresenceEvery);

        static int Clamp(int value, int low, int high)
        {
            if (value < low)
            {
                return low;
            }

            if (value > high)
            {
                return high;
            }

            return value;
        }
    }
}
