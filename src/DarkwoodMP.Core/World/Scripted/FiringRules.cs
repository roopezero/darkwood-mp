namespace DarkwoodMP.World.Scripted
{
    /// Sharing a trigger our player set off as its events start running
    /// (the plugin's GameEventsFirePatch).
    public static class FiringRules
    {
        /// What to do as the firing trigger's events start. Each trigger is
        /// looked at once. Events that ran once already, no sharing, and a
        /// client's world trigger (the host's to send) run as the game
        /// runs them.
        public static FiringShare Share(in FiringFacts f)
        {
            if (!f.Firing || f.Index < 0 || !f.HasTriggers
                || f.Current != EventPart.All)
            {
                return FiringShare.Pass;
            }

            if (TriggerRules.Spent(f.Fired, f.MultipleFire))
            {
                return FiringShare.Game;
            }

            if (!f.Sharing)
            {
                return FiringShare.Game;
            }

            if (f.World && f.Client)
            {
                return FiringShare.Game;
            }

            if (f.World && f.By != 0)
            {
                return FiringShare.Caused;
            }

            return FiringShare.Send;
        }

        /// Whether the game runs the events itself after sending: not on a
        /// client that sent them (it runs its part on the host's yes).
        public static bool RunsAtOnce(bool client, bool sent)
        {
            return !(client && sent);
        }

        /// After the host's yes: our player walked in and out of a
        /// repeatable area again before it came, so its exit ran here
        /// already and only the shared part of the enter runs now.
        public static bool LeftBeforeYes(
            bool repeatableArea,
            int exitedBefore,
            int exited)
        {
            return repeatableArea && exited > exitedBefore;
        }
    }
}
