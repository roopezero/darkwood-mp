namespace DarkwoodMP.World.Building
{
    /// Building and repairing at construction sites (the plugin's
    /// Construction). A joiner asks the host first: the ingredients go at
    /// once, the build once the host agrees, since the game can't
    /// un-build.
    public static class ConstructionRules
    {
        /// How far from a construction a client's player may build it
        /// (WorldActions.Reach).
        public const float Reach = ReachRules.Reach;

        /// The construct call's forceOption when it names none.
        public const int NoOption = -1;

        /// What Constructible.construct does (Construction.BeforeBuild). The
        /// game's own builds, the joiner's confirmed copy and anything
        /// while not sharing aren't shared; only the player's (or a test's
        /// as the player) are.
        public static BuildStart Start(in StartFacts build)
        {
            if (build.Confirming || !build.Sharing)
            {
                return BuildStart.Game;
            }

            if (!build.Manual && !build.TestAsPlayer)
            {
                return BuildStart.Game;
            }

            if (!build.Client)
            {
                return BuildStart.Host;
            }

            if (build.Waiting)
            {
                return BuildStart.Swallow;
            }

            return BuildStart.Client;
        }

        /// Client: whether to ask the construction menu for the option
        /// first (getSelectedOption sets chosenOption): a player's build
        /// that names no option.
        public static bool ReadsMenu(int forceOption, bool manual)
        {
            return forceOption == NoOption && manual;
        }

        /// The option built: forceOption, or chosenOption when it names
        /// none. The host's postfix sends this one too.
        public static int OptionOf(int forceOption, int chosen)
        {
            if (forceOption == NoOption)
            {
                return chosen;
            }

            return forceOption;
        }

        /// Whether a construction has this option.
        public static bool HasOption(int option, int options)
        {
            return option >= 0 && option < options;
        }

        /// Client: whether it asks the host (the game's build skipped), or
        /// builds here alone as the game would: an option it doesn't have,
        /// or a construction it can't name to the host.
        public static bool Asks(int option, int options, bool named)
        {
            return HasOption(option, options) && named;
        }

        /// Host: a client's Construct. The checks run in this order, and
        /// the first that fails wins. A broken construction may be built
        /// again whatever it says.
        public static Verdict MayBuild(in BuildFacts build)
        {
            if (!build.Found)
            {
                return Verdict.Refuse(Refusal.NothingToBuild);
            }

            if (!HasOption(build.Option, build.Options))
            {
                var option = build.Option.ToString();
                return Verdict.Refuse(Refusal.NoSuchOption, option);
            }

            if (build.OnlyWhenDestroyed && !build.Broken)
            {
                return Verdict.Refuse(Refusal.NotBroken);
            }

            if (build.Constructed && !build.Broken)
            {
                return Verdict.Refuse(Refusal.BuiltAlready);
            }

            var reach = build.Reach with { Reach = Reach, What = build.Name };
            return ReachRules.InReach(reach);
        }

        /// Either side, once the message is taken and, on the host,
        /// allowed: whether to build this game's copy. The host passes it
        /// on either way.
        public static bool Builds(bool found, int option, int options)
        {
            return found && HasOption(option, options);
        }
    }
}
