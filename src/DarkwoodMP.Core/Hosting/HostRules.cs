namespace DarkwoodMP.Hosting
{
    /// Whether and how the player hosts: Plugin.CantHostReason, the Start
    /// Game window's host box (Plugin.HostWhenWorldReady), Plugin.OpensPort
    /// and Plugin.HostDirect's settings.
    public static class HostRules
    {
        /// How long the host box waits, in real time, for the world's load
        /// to take the game away from the main menu.
        public const float LoadStartWait = 20f;

        /// Why the player can't host the world they're in, or Allow. A host
        /// shares the world it's in, so hosting starts from inside a world:
        /// from the main menu a server would only turn joiners away. Not in
        /// a world wins, then a copy of another host's world, then the
        /// epilogue.
        public static Verdict MayHost(in HostFacts host)
        {
            if (!host.InWorld)
            {
                return Verdict.Refuse(Refusal.LoadWorldToHost);
            }

            if (host.HostsCopy)
            {
                return Verdict.Refuse(Refusal.HostsCopy);
            }

            if (host.InEpilogue)
            {
                return Verdict.Refuse(Refusal.EpilogueBegun);
            }

            return Verdict.Allow;
        }

        /// One frame of the host box's wait for the world. First it waits
        /// while the main menu is still up, for at most LoadStartWait; then
        /// for the world, giving up if the game is back at the main menu.
        /// Both checks run in the frame the first wait ends, so after 20 s
        /// still at the menu it gives up at once.
        public static HostWaitStep Wait(in HostWaitFacts wait)
        {
            if (!wait.LoadStarted
                && wait.MainMenuReady
                && wait.Waited.Value < LoadStartWait)
            {
                return HostWaitStep.WaitForLoad;
            }

            if (!wait.WorldReady)
            {
                if (wait.MainMenuReady)
                {
                    return HostWaitStep.BackAtMenu;
                }

                return HostWaitStep.WaitForWorld;
            }

            return HostWaitStep.WorldReady;
        }

        /// How the host box hosts once the world is ready. A running session
        /// wins, then the reason the player can't host. Steam asked for but
        /// not up hosts with Direct, and only the log says so.
        public static HostBegin Begin(in BeginFacts begin)
        {
            if (begin.InSession)
            {
                return new HostBegin { Step = HostBeginStep.InSession };
            }

            var why = MayHost(begin.Host);
            if (why.Refused)
            {
                return new HostBegin
                {
                    Step = HostBeginStep.CantHost,
                    Why = why,
                };
            }

            if (begin.SteamAsked && begin.SteamUp)
            {
                return new HostBegin { Step = HostBeginStep.Steam };
            }

            return new HostBegin { Step = HostBeginStep.Direct };
        }

        /// Whether Direct hosting asks the router to forward the port. The
        /// fake router of --dwmp-upnp-test (PortMapper.TestRouter set) wins
        /// over everything; otherwise the OpenPort setting, never in a test
        /// game (ModHost.TestMode), which hosts over and over.
        public static bool OpensPort(
            bool testRouter,
            bool openPort,
            bool testMode)
        {
            if (testRouter)
            {
                return true;
            }

            return openPort && !testMode;
        }

        /// The password Direct hosting uses: one given for this time
        /// (--dwmp-password), even an empty one, else the Password setting.
        /// Only null counts as not given.
        public static string PasswordOf(string given, string setting)
        {
            if (given != null)
            {
                return given;
            }

            return setting;
        }

        /// The player limit Direct hosting uses: one given for this time
        /// when above zero (--dwmp-max-players), else the MaxPlayers setting.
        public static int PlayersOf(int given, int setting)
        {
            if (given > 0)
            {
                return given;
            }

            return setting;
        }
    }
}
