namespace DarkwoodMP.World.Progress
{
    /// Which game keeps the shared progression, and what each side does
    /// when its player cooks, takes a level or resets (Progression).
    public static class ProgressSideRules
    {
        /// Progression.Update's pass runs once a second. The map step
        /// skills-give waits 1.5 s for it.
        public static readonly Seconds SyncEvery = new Seconds(1f);

        /// Playing alone: no active session, in a world, not in the host's.
        public static bool Solo(in SideFacts side)
        {
            return !side.SessionActive && side.InWorld && !side.ClientMode;
        }

        /// The host, or playing alone: our game keeps the shared numbers.
        public static bool HostSide(in SideFacts side)
        {
            return side.IsHost || Solo(side);
        }

        /// A client in the host's world that has had the shared state.
        public static bool ClientActive(in SideFacts side)
        {
            return side.ClientMode && side.InWorld && side.Known;
        }

        /// Our player cooked this much essence (Progression.Cooked).
        public static ProgressRoute Cooked(in SideFacts side, int amount)
        {
            if (amount <= 0 || !side.HasPlayer)
            {
                return ProgressRoute.None;
            }

            return Either(side);
        }

        /// Our player took a level (Progression.LevelTaken).
        public static ProgressRoute LevelTaken(in SideFacts side)
        {
            return Either(side);
        }

        /// Our player's skills were reset (Progression.DoReset, after its
        /// player check): any client sends its level, whether or not it has
        /// had the shared state; everyone else syncs as the host.
        public static ProgressRoute AfterReset(in SideFacts side)
        {
            if (side.ClientMode)
            {
                return ProgressRoute.Client;
            }

            return ProgressRoute.Host;
        }

        /// Whether the once-a-second pass is due: not before next.
        public static bool Due(Seconds now, Seconds next)
        {
            return !(now < next);
        }

        /// When the pass after this one is due.
        public static Seconds Next(Seconds now)
        {
            return new Seconds(now.Value + SyncEvery.Value);
        }

        /// What the pass does (Progression.Update). The solo branch looks
        /// at the record after the main-menu branch has forgotten it.
        public static ProgressTick Update(in SideFacts side)
        {
            bool forgets = !side.InWorld && !side.SessionActive && side.HasPath;
            bool record = side.HasRecord && !forgets;
            var route = ProgressRoute.None;
            if (side.IsHost && side.Together)
            {
                route = ProgressRoute.Host;
            }
            else if (Solo(side) && record)
            {
                route = ProgressRoute.Host;
            }
            else if (ClientActive(side))
            {
                route = ProgressRoute.Client;
            }

            return new ProgressTick { Forgets = forgets, Route = route };
        }

        static ProgressRoute Either(in SideFacts side)
        {
            if (ClientActive(side))
            {
                return ProgressRoute.Client;
            }

            if (HostSide(side))
            {
                return ProgressRoute.Host;
            }

            return ProgressRoute.None;
        }
    }
}
