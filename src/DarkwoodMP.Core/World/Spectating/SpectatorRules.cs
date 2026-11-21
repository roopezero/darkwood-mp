namespace DarkwoodMP.World.Spectating
{
    /// A player dead for good watches the others instead of the game's
    /// permadeath, while anyone is still playing (Spectator).
    public static class SpectatorRules
    {
        /// How long (unscaled) a joiner dead for good waits for someone to
        /// watch before the permadeath.
        public const float DeadWait = 20f;

        /// With nobody left to watch for over this long (unscaled), the
        /// permadeath.
        public const float NobodyFor = 3f;

        /// Whether the permadeath screen gives way to watching
        /// (Spectator.TryBegin).
        public static bool MayBegin(in BeginFacts facts)
        {
            if (facts.AllowPermadeath || !facts.InSession || !facts.InWorld)
            {
                return false;
            }

            return facts.Playing != 0;
        }

        /// One round of a dead joiner's wait (Spectator.WaitAndBegin). The
        /// deadline is checked first.
        public static DeadWaitStep DeadWaits(in DeadWaitFacts facts)
        {
            if (!(facts.Now < facts.Until))
            {
                return DeadWaitStep.Permadeath;
            }

            if (!facts.InWorld || facts.Active)
            {
                return DeadWaitStep.Stops;
            }

            if (facts.Playing > 0)
            {
                return DeadWaitStep.Begins;
            }

            return DeadWaitStep.Waits;
        }

        /// Whether a player who crawled to their death in the epilogue
        /// watches the others (Spectator.BeginWatch).
        public static bool MayWatch(bool active, bool session, int playing)
        {
            return !active && session && playing != 0;
        }

        /// One frame of watching (Spectator.Run). Out of the session
        /// nobody counts as playing. In the epilogue nobody to watch only
        /// stays, and the wait for nobody isn't touched.
        public static WatchStep Watch(in WatchFacts facts)
        {
            var playing = 0;
            if (facts.InSession)
            {
                playing = facts.Playing;
            }

            if (playing == 0 && facts.Epilogue)
            {
                return Step(WatchAction.Stay, facts.Nobody, facts.NobodySince);
            }

            if (playing != 0)
            {
                return default;
            }

            if (!facts.InWorld)
            {
                return Step(WatchAction.Ended, facts.Nobody, facts.NobodySince);
            }

            if (!facts.InSession)
            {
                return Step(
                    WatchAction.SessionLost,
                    facts.Nobody,
                    facts.NobodySince);
            }

            var since = facts.NobodySince;
            if (!facts.Nobody)
            {
                since = facts.Now;
            }

            if (facts.Now.Value - since.Value > NobodyFor)
            {
                return Step(WatchAction.NobodyLeft, true, since);
            }

            return Step(WatchAction.Waits, true, since);
        }

        /// The index of the player to watch (Spectator.Run): the one
        /// watched (found, -1 when gone, which counts as the first), moved
        /// by the player's choice, wrapping round. count is above 0.
        public static int Pick(int found, int count, Choice choice)
        {
            var i = found;
            if (i < 0)
            {
                i = 0;
            }

            if (choice == Choice.Next)
            {
                return (i + 1) % count;
            }

            if (choice == Choice.Previous)
            {
                return (i + count - 1) % count;
            }

            return i;
        }

        static WatchStep Step(WatchAction action, bool nobody, Seconds since)
        {
            return new WatchStep
            {
                Action = action,
                Nobody = nobody,
                NobodySince = since,
            };
        }
    }
}
