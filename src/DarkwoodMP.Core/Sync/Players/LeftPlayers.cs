using System.Collections.Generic;

namespace DarkwoodMP.Sync.Players
{
    /// The players who left, and when (unscaled), so that a PlayerState
    /// sent before PlayerLeft and arriving after it doesn't bring back a
    /// frozen puppet (PlayerSync._left). One per session.
    public sealed class LeftPlayers
    {
        readonly Dictionary<ulong, Seconds> _left =
            new Dictionary<ulong, Seconds>();

        /// The player left at now (Session.OnPeerDisconnected).
        public void Left(ulong id, Seconds now)
        {
            _left[id] = now;
        }

        /// The player came back (Session.OnPeerConnected), which on a
        /// client is raised only for the host, at Welcome.
        public void Joined(ulong id)
        {
            _left.Remove(id);
        }

        /// The session ended.
        public void Clear()
        {
            _left.Clear();
        }

        /// The player left less than LeftIgnore ago. The first ask after
        /// that forgets them.
        public bool JustNow(ulong id, Seconds now)
        {
            if (!_left.TryGetValue(id, out var at))
            {
                return false;
            }

            if (now.Value - at.Value < PlayerStateRules.LeftIgnore)
            {
                return true;
            }

            _left.Remove(id);
            return false;
        }
    }
}
