namespace DarkwoodMP.World.Places
{
    /// The host, loading and keeping separate locations for the joiners
    /// in them (the plugin's Locations), so that its game runs their
    /// enemies while its own player is elsewhere.
    public static class HostLocationRules
    {
        /// Seconds a location loading for the joiners may take
        /// (realtime).
        public const float LoadTimeout = 60f;

        /// Seconds between two looks at where the joiners are (unscaled).
        public const float KeepEvery = 0.5f;

        /// A location loading for the joiners, each frame.
        public static LoadStep Loading(bool loaded, Seconds now, Seconds since)
        {
            if (loaded)
            {
                return LoadStep.Loaded;
            }

            if (now.Value - since.Value > LoadTimeout)
            {
                return LoadStep.GaveUp;
            }

            return LoadStep.Wait;
        }

        /// Whether the next promised location may start loading: one is
        /// queued and the game isn't loading or dreaming. A name loaded
        /// already is dropped from the queue (MayLoad).
        public static bool MayStart(in StartFacts start)
        {
            if (start.Queued == 0 || start.Loading)
            {
                return false;
            }

            if (start.Dreaming || start.DreamPrepared)
            {
                return false;
            }

            return !start.SwitchingDream;
        }

        /// Whether a name taken from the queue loads: it isn't loaded.
        public static bool MayLoad(bool loaded)
        {
            return !loaded;
        }

        /// Whether to look at where the joiners are now.
        public static bool KeepDue(Seconds now, Seconds next)
        {
            return !(now.Value < next.Value);
        }

        /// When the next look is due.
        public static Seconds NextKeep(Seconds now)
        {
            return new Seconds(now.Value + KeepEvery);
        }

        /// Whether a joiner's stand-in asks for its location: it has one,
        /// and it isn't the shared dream, which runs with our player.
        public static bool Wanted(bool proxy, bool location, bool dream)
        {
            return proxy && location && !dream;
        }

        /// How a wanted location is entered.
        public static EnterStep EnterFor(bool entered, bool active)
        {
            if (!active)
            {
                return EnterStep.ForceEnter;
            }

            if (!entered)
            {
                return EnterStep.Enter;
            }

            return EnterStep.None;
        }

        /// Whether a kept location nobody wants any more is left: not
        /// while our own player is in it.
        public static bool LeavesUnwanted(bool ours)
        {
            return !ours;
        }

        /// Location.leave on the host. A kept location is never left, even
        /// with force; its exit events run only when not forced.
        public static LeaveStep BeforeLeave(in LeaveFacts leave)
        {
            if (!leave.Kept)
            {
                return LeaveStep.Leave;
            }

            if (leave.Force || !leave.Entered || leave.LoadingGame)
            {
                return LeaveStep.Stay;
            }

            if (leave.Dreaming || leave.WantToDream)
            {
                return LeaveStep.Stay;
            }

            return LeaveStep.StayWithExitEvents;
        }

        /// Location.enter on the host: whether our player entering a
        /// location entered for a joiner runs its enter events itself.
        public static bool RunsEnterEvents(in EnterFacts enter)
        {
            if (enter.Force || !enter.Entered)
            {
                return false;
            }

            return enter.Kept && !enter.Quiet;
        }

        /// Location.checkEnterEvents: skipped once for a quiet location.
        public static bool RunsGameEnterEvents(bool quiet)
        {
            return !quiet;
        }

        /// The save: a kept location our player isn't in is saved as not
        /// entered.
        public static bool SavesAsLeft(bool kept, bool ours)
        {
            return kept && !ours;
        }

        /// Whether the grid our player was on goes back after the location
        /// loading for the joiners joined its grid.
        public static bool PutsGridBack(in GridFacts grid)
        {
            if (grid.Loading == null || !grid.HasLocation)
            {
                return false;
            }

            if (grid.Before == null || grid.TrueName != grid.Loading)
            {
                return false;
            }

            return grid.HasGrid && grid.Grid != grid.Before;
        }
    }
}
