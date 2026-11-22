namespace DarkwoodMP.World
{
    /// The guards every shared world call goes through (the plugin's
    /// Guards): how deep in patched calls a game is, so only the outermost
    /// call is sent, and whether the world is shared at all.
    public static class GuardRules
    {
        /// Leaving a patched call at this depth: it was the outermost when
        /// no call is left around it. A depth that went wrong (a call that
        /// threw, a reset in between) counts as outermost.
        public static bool IsOutermost(int depth) => depth <= 1;

        /// The depth after leaving a patched call: one less, never below
        /// none.
        public static int Leave(int depth)
        {
            if (depth - 1 > 0)
            {
                return depth - 1;
            }

            return 0;
        }

        /// Whether this game shares its world right now: not while making
        /// a call from another game, only in a loaded world that is not
        /// loading, and only as a host with players in or a client in the
        /// host's world. While a world loads, the save loader makes the
        /// saved dropped items and thrown things again, and a joiner
        /// sending those as new would make the host a second copy of each.
        public static bool Sharing(in GuardFacts game)
        {
            if (game.Replaying || !game.InWorld || game.Loading)
            {
                return false;
            }

            return (game.Host && game.Peers > 0) || game.Client;
        }
    }
}
