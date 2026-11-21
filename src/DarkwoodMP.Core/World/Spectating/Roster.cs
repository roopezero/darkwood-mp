using System.Collections.Generic;

namespace DarkwoodMP.World.Spectating
{
    /// Who a dead player can watch, with when each remote was last seen
    /// alive (Spectator._lastAlive and Spectator.Playing). One per world;
    /// Reset clears it.
    public sealed class Roster
    {
        /// A player who died this many seconds ago (unscaled) or less is
        /// still watched: the moment of death is worth seeing.
        public const float Grace = 10f;

        readonly Dictionary<ulong, Seconds> _lastAlive =
            new Dictionary<ulong, Seconds>();

        /// The owners of the players to watch, sorted by owner: every
        /// remote but the dummy and those spectating, alive or dead for
        /// less than Grace. Notes the alive ones' time first, spectating
        /// or not.
        public List<ulong> Playing(IReadOnlyList<Watched> remotes, Seconds now)
        {
            var list = new List<ulong>();
            for (var i = 0; i < remotes.Count; i++)
            {
                var remote = remotes[i];
                if (remote.Dummy)
                {
                    continue;
                }

                if (remote.Alive)
                {
                    _lastAlive[remote.Owner] = now;
                }

                if (remote.Spectating)
                {
                    continue;
                }

                if (remote.Alive || Recent(remote.Owner, now))
                {
                    list.Add(remote.Owner);
                }
            }

            list.Sort();
            return list;
        }

        /// Forget when everyone was alive (Spectator.Reset).
        public void Reset()
        {
            _lastAlive.Clear();
        }

        bool Recent(ulong owner, Seconds now)
        {
            if (!_lastAlive.TryGetValue(owner, out var seen))
            {
                return false;
            }

            return now.Value - seen.Value < Grace;
        }
    }
}
