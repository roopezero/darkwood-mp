namespace DarkwoodMP.World.Places
{
    /// Where a player is, and who runs it (the plugin's Locations): the
    /// shared world, a separate location, or a dream. The host runs the
    /// world, the locations it placed and the shared level dream.
    public static class PlaceRules
    {
        /// What a place in a dream starts with.
        public const string DreamPrefix = "dream:";

        /// The place with no grid.
        public const string Unknown = "?";

        /// How near a joiner must stand to a location the host has to be
        /// in it (on the ground plane, strict).
        public const float LocationReach = 15000f;

        /// Where our player is: "" in the shared world, else the grid's
        /// name ("?" with none), after DreamPrefix in a dream.
        public static string Current(in WhereFacts where)
        {
            if (!where.Dreaming && !where.InLocation)
            {
                return "";
            }

            string grid = where.Grid;
            if (grid == null)
            {
                grid = Unknown;
            }

            if (where.Dreaming)
            {
                return DreamPrefix + grid;
            }

            return grid;
        }

        /// Whether another player is in the same place as ours. Someone
        /// else's own dream is never ours.
        public static bool SamePlace(in SameFacts same)
        {
            if (!same.Away || !same.MeAway)
            {
                return same.Away == same.MeAway;
            }

            if (string.IsNullOrEmpty(same.Place) || same.Place == Unknown)
            {
                return false;
            }

            if (same.Place.StartsWith(DreamPrefix) && !same.DreamShared)
            {
                return false;
            }

            return same.Place == same.Current;
        }

        /// A client: whether the host runs our player's area. A location
        /// we gave up asking for stands elsewhere in the host's game.
        public static bool HostRunsHere(in HereFacts here)
        {
            if (here.Dreaming)
            {
                return here.SharedDream && !here.DreamGivenUp;
            }

            if (!here.InLocation)
            {
                return true;
            }

            return !here.LocationGivenUp;
        }

        /// The host: what a joiner's place names.
        public static PlaceKind Kind(string place)
        {
            if (string.IsNullOrEmpty(place) || place == Unknown)
            {
                return PlaceKind.None;
            }

            if (place.StartsWith(DreamPrefix))
            {
                return PlaceKind.Dream;
            }

            return PlaceKind.Location;
        }

        /// The host: whether a joiner's dream is the shared one we run: we
        /// dream it too, and it is our place.
        public static bool RunsDream(bool shared, string place, string ours)
        {
            return shared && place == ours;
        }

        /// The host: whether a loaded location (by its key in
        /// spawnedLocations, and its true name) is the joiner's place.
        public static bool Matches(string key, string trueKey, string place)
        {
            return key == place || trueKey == place;
        }

        /// The host: whether a joiner stands in the location there.
        public static bool InReach(Vec3 joiner, Vec3 location)
        {
            float x = joiner.X - location.X;
            float z = joiner.Z - location.Z;
            return x * x + z * z < LocationReach * LocationReach;
        }

        /// Whether our player's dream is the shared level dream.
        public static bool IsSharedDream(in DreamFacts dream)
        {
            if (!dream.Dreaming || !dream.HasLocation || dream.Shared == null)
            {
                return false;
            }

            return dream.HasPreset && dream.Preset == dream.Shared;
        }

        /// Whether our player is in a later part of the epilogue played
        /// together (its flat, its crater: any part after the first, which
        /// the host runs). Every game builds each part for itself, and what
        /// a player does there stays in their own game, the host's too: the
        /// joiners' triggers there were already their own, and the host's
        /// that reached them used up the once-only events of their flat
        /// (its clothes, its rooms' lights), so a joiner who came second
        /// had none of it and froze petting the dog. The parts still move
        /// on together (DreamSync).
        public static bool IsLaterPart(in DreamFacts dream)
        {
            if (!dream.Epilogue || !dream.Dreaming || !dream.HasLocation
                || dream.Shared == null || !dream.HasPreset)
            {
                return false;
            }

            return dream.Preset != dream.Shared;
        }

        /// The host: whether our own player is in this location now, by
        /// true names. Ours is false with no location or OutsideLocations.
        public static bool OursNow(bool inside, string ours, string theirs)
        {
            return inside && ours == theirs;
        }
    }
}
