using System;

namespace DarkwoodMP.World.Places
{
    /// Where a separate location stands (the plugin's Locations). Each
    /// game puts a location at the next free spot and turns it at random,
    /// so the host decides both for everyone: a client asks before it
    /// loads one, and waits a while for the answer.
    public static class LocationSlotRules
    {
        /// Seconds a client waits for the host's spot (realtime).
        public const float AskTimeout = 10f;

        /// The spots the game itself has (OutsideLocations).
        public const int GameSpots = 21;

        /// Spots kept free past the next one.
        public const int Margin = 8;

        /// The extra spots' rows.
        public const int RowLength = 7;

        /// How the host answers a joiner's ask, in its order.
        public static AskAnswer Answer(in AskFacts ask)
        {
            if (!ask.IsHost || !ask.InWorld)
            {
                return AskAnswer.Ignore;
            }

            if (!ask.HasLocations || string.IsNullOrEmpty(ask.Name))
            {
                return AskAnswer.Ignore;
            }

            if (ask.Known)
            {
                return AskAnswer.Known;
            }

            if (ask.Loaded)
            {
                return AskAnswer.Loaded;
            }

            if (!ask.Real)
            {
                return AskAnswer.NoSuchLocation;
            }

            return AskAnswer.NewSpot;
        }

        /// Whether a new spot is loaded in the host's game too: not for a
        /// dream, which is the joiner's alone.
        public static bool LoadsHere(bool dream)
        {
            return !dream;
        }

        /// Whether a client's ask is for a dream (its first byte's bit 0).
        public static bool AsksForDream(bool prepared, bool dreaming)
        {
            return prepared || dreaming;
        }

        /// How many spots the list needs before a location loads, with an
        /// active session, host or client.
        public static int Need(int spawned)
        {
            return spawned + Margin;
        }

        /// The spot added to a list of this many: rows of RowLength past
        /// the game's own, 30000 apart, north and south in turn. A list
        /// shorter than GameSpots gets the first extra spot.
        public static Vec3 ExtraSpot(int count)
        {
            int extra = count - GameSpots;
            if (extra < 0)
            {
                extra = 0;
            }

            int row = extra / RowLength;
            int col = extra % RowLength;
            float z = -110000f - 30000f * (row / 2);
            if (row % 2 == 0)
            {
                z = 80000f + 30000f * (row / 2);
            }

            return new Vec3(-75000f + 25000f * col, 0f, z);
        }

        /// A loaded location's turn, from its y angle, as Mathf.RoundToInt
        /// (halves to even).
        public static int TurnOf(float y)
        {
            return (int)Math.Round(y);
        }

        /// Whether a client's wait for the host goes on.
        public static bool Waits(bool wait, Seconds now, Seconds until)
        {
            return wait && now.Value < until.Value;
        }

        /// When a client's wait ends.
        public static Seconds WaitUntil(Seconds now)
        {
            return new Seconds(now.Value + AskTimeout);
        }

        /// Whether a client asks as the trip to a location starts
        /// (prepareLocation): it must wait for it and hasn't loaded it.
        public static bool AsksEarly(bool wait, bool loaded)
        {
            return wait && !loaded;
        }

        /// In spawnLocation with no spot known: whether the host promises
        /// its own load's spot, with a turn the shell rolls then.
        public static bool PromisesOwn(bool active, bool host, string name)
        {
            return active && name != null && host;
        }
    }
}
