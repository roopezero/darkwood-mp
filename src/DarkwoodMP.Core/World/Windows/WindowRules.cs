namespace DarkwoodMP.World.Windows
{
    /// The host's check of what a client did to a window (the window part
    /// of the plugin's WorldActions.Allowed), whether a window call is
    /// sent, and what a client's window is set back to when the host
    /// refused it. The first check that fails wins: is the window there,
    /// is the player in reach, then the window's state.
    public static class WindowRules
    {
        /// How far from a window a player may be to use it.
        public const float Reach = ReachRules.Reach;

        /// How far a player may be from a window they hit.
        public const float ShotReach = ReachRules.ShotReach;

        /// More damage than this in one hit is more than any weapon does.
        public const float MaxHit = 1000f;

        /// Host: a client's hammer stroke barricaded the window.
        public static Verdict MayBarricade(in WindowFacts window)
        {
            var verdict = Present(window, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (window.Barricaded)
            {
                return Verdict.Refuse(Refusal.WindowBarricaded);
            }

            return Verdict.Allow;
        }

        /// Host: a client broke the window's barricade.
        public static Verdict MayDestroyBarricade(in WindowFacts window)
        {
            var verdict = Present(window, Reach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (!window.Barricaded)
            {
                return Verdict.Refuse(Refusal.WindowNotBarricaded);
            }

            return Verdict.Allow;
        }

        /// Host: a client hit the window's barricade for this much damage.
        /// Neither a negative hit nor a window with no barricade is
        /// refused.
        public static Verdict MayHit(in WindowFacts window, float damage)
        {
            var verdict = Present(window, ShotReach);
            if (verdict.Refused)
            {
                return verdict;
            }

            if (damage > MaxHit)
            {
                return Verdict.Refuse(
                    Refusal.HitTooLarge,
                    damage.ToString("0"));
            }

            return Verdict.Allow;
        }

        /// Host: the check for any window action, by its value, with the
        /// message's x (a hit's damage). A value with no check of its own
        /// (14 to 20) only needs the window there and in reach.
        public static Verdict MayAct(
            in WindowFacts window,
            WindowAction action,
            float x)
        {
            switch (action)
            {
                case WindowAction.Barricade:
                    return MayBarricade(window);
                case WindowAction.DestroyBarricade:
                    return MayDestroyBarricade(window);
                case WindowAction.Hit:
                    return MayHit(window, x);
                default:
                    return Present(window, Reach);
            }
        }

        /// Barricade, destroy barricade and hit: sent from the outermost
        /// call, while sharing, when the window changed.
        public static bool SendsChange(in WindowSendFacts send) =>
            send.Outermost && send.Sharing && !send.Before.Equals(send.After);

        /// Client: what to do to our window to match the host's, from
        /// whether each has a barricade.
        public static WindowFix RollBack(bool host, bool ours)
        {
            if (host && !ours)
            {
                return WindowFix.Barricade;
            }

            if (!host && ours)
            {
                return WindowFix.RemoveBarricade;
            }

            return WindowFix.None;
        }

        /// The window is there and the requester within reach of it.
        static Verdict Present(in WindowFacts window, float reach)
        {
            if (!window.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchWindow);
            }

            var requester = window.Requester with { Reach = reach };
            return ReachRules.InReach(requester);
        }
    }
}
