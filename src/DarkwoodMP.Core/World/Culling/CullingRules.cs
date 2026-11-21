namespace DarkwoodMP.World.Culling
{
    /// The host keeps the world's culling grid entered round every player,
    /// not only its own (SharedCullingPatch).
    public static class CullingRules
    {
        /// How far past the screen the host's own area reaches, times the
        /// resolution modifier.
        public const float ScreenMargin = 500f;

        /// How often the others' areas are entered while the host's player
        /// is being taken to a place.
        public const float LoadingEvery = 0.5f;

        /// Which refresh runs: the game's with no grid; with no stand-ins
        /// the game's for an instant one or with no active session, else
        /// our own pass; with stand-ins the shared one.
        public static RefreshKind Which(in RefreshFacts refresh)
        {
            if (!refresh.Grid)
            {
                return RefreshKind.Game;
            }

            if (!refresh.StandIns)
            {
                if (refresh.Instant || !refresh.Session)
                {
                    return RefreshKind.Game;
                }

                return RefreshKind.OwnPass;
            }

            return RefreshKind.Shared;
        }

        /// The host's own area: (width + 500 m) / zoom by (height + 500 m)
        /// / zoom, m the larger resolution modifier, cut to whole units.
        public static ViewSize HostArea(in ScreenFacts screen)
        {
            float m = Floats.Max(screen.WidthModifier, screen.HeightModifier);
            float zoom = screen.Zoom;
            return new ViewSize
            {
                Dx = (int)((screen.Width + ScreenMargin * m) / zoom),
                Dy = (int)((screen.Height + ScreenMargin * m) / zoom),
            };
        }

        /// Whether a grid node is in a player's area: within Dx along x
        /// and Dy along z of them, edges included.
        public static bool Near(Vec2 node, Vec2 player, ViewSize area) =>
            Abs(player.X - node.X) <= area.Dx
            && Abs(player.Z - node.Z) <= area.Dy;

        /// A remote player's grid: their location's when they are in one
        /// (it wins over Away), none when away, else the world's.
        public static PlayerGrid GridOf(bool inLocation, bool away)
        {
            if (inLocation)
            {
                return PlayerGrid.Location;
            }

            if (away)
            {
                return PlayerGrid.None;
            }

            return PlayerGrid.World;
        }

        /// Whether the host's player is away: the world's grid exists and
        /// isn't the current one.
        public static bool HostAway(bool world, bool worldIsCurrent) =>
            world && !worldIsCurrent;

        /// Whether a spread pass enters a node near a player: always when
        /// forced, else only one not entered yet. Nodes not near are left
        /// after every enter.
        public static bool Enters(bool force, bool entered) =>
            force || !entered;

        /// Whether the others' areas are entered this frame while the
        /// host's player is being taken to a place, and when next: every
        /// LoadingEvery of real time, only while all the gates hold (the
        /// timer doesn't move otherwise).
        public static Tick WhileLoading(in LoadingFacts load)
        {
            bool gates = load.StandIns
                && load.Loading
                && load.Refreshes
                && load.Player
                && !load.LoadingGame
                && load.WorldMade;
            if (!gates)
            {
                return new Tick { Due = false, Next = load.Next };
            }

            return Tick.At(load.Now, load.Next, new Seconds(LoadingEvery));
        }

        /// As Mathf.Abs.
        static float Abs(float x)
        {
            if (x < 0f)
            {
                return -x;
            }

            return x;
        }
    }
}
