namespace DarkwoodMP.Sync.Players
{
    /// When our game sends our player's PlayerState, and what it does with
    /// one that arrives (PlayerSync).
    public static class PlayerStateRules
    {
        /// Seconds (unscaled) a player who left stays ignored: a state that
        /// arrives within it is dropped.
        public const float LeftIgnore = 5f;

        /// The least send rate: 1 a second.
        public const int MinSendRate = 1;

        /// Seconds (unscaled) between two periodic sends at a rate a
        /// second (Plugin.SendRate, read at each send).
        public static Seconds SendInterval(int rate)
        {
            int perSecond = rate;
            if (perSecond < MinSendRate)
            {
                perSecond = MinSendRate;
            }

            return new Seconds(1f / perSecond);
        }

        /// The periodic send is due.
        public static bool SendDue(Seconds now, Seconds next) =>
            now.Value >= next.Value;

        /// Our state is sent while the session is active and connected,
        /// and not while our character is still being placed: a joiner
        /// isn't shown before then. Checked after the capture.
        public static bool MaySend(in SendFacts send)
        {
            if (!send.Active || !send.Connected)
            {
                return false;
            }

            return !send.Placing;
        }

        /// Whose "left just now" entry a state is checked against: the
        /// sender's on the host, the owner it says on a client.
        public static ulong GuardKey(bool host, ulong from, ulong said)
        {
            if (host)
            {
                return from;
            }

            return said;
        }

        /// What happens to a PlayerState that arrived. Left just now is
        /// checked first on both sides. The host then checks the position
        /// and relays it; a client drops one not from the host. Either
        /// drops a state about our own player, after the host's relay.
        public static StateArrival Arrive(in StateFacts state)
        {
            ulong owner = state.Said;
            if (state.Host)
            {
                owner = state.From;
            }

            if (state.LeftJustNow)
            {
                return new StateArrival
                {
                    Owner = owner,
                    Drop = StateDrop.LeftJustNow,
                };
            }

            var move = StateMove.None;
            if (state.Host)
            {
                move = StateMove.Check;
                if (state.Spectating)
                {
                    move = StateMove.Forget;
                }
            }
            else if (state.From != state.HostId)
            {
                return new StateArrival
                {
                    Owner = owner,
                    Drop = StateDrop.NotFromHost,
                };
            }

            var drop = StateDrop.None;
            if (owner == state.LocalId)
            {
                drop = StateDrop.AboutUs;
            }

            return new StateArrival
            {
                Owner = owner,
                Move = move,
                Relays = state.Host,
                Drop = drop,
            };
        }
    }
}
