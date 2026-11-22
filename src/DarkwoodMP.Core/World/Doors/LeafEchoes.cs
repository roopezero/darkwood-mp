using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Doors
{
    /// Client: the angles we sent for one door's leaf, oldest first, until
    /// the host passes each back (the plugin's DoorPush._unechoed, one
    /// list per door). The host sends every angle to the pusher too; a
    /// client skips those echoes, which would turn its leaf back while
    /// its player still pushes it.
    public sealed class LeafEchoes
    {
        /// At most this many angles are kept; later ones aren't.
        public const int MaxKept = 64;

        /// An angle this near one we sent is its echo.
        public const float Match = 0.01f;

        readonly List<float> _sent = new List<float>();

        /// No angle waits for its echo: the shell drops this door's list.
        public bool Empty => _sent.Count == 0;

        /// We sent this angle to the host.
        public void Sent(float angle)
        {
            if (_sent.Count < MaxKept)
            {
                _sent.Add(angle);
            }
        }

        /// The host sent this angle: true if it's the echo of one we sent,
        /// which is dropped with every older one. An angle that matches
        /// none forgets them all.
        public bool IsEcho(float angle)
        {
            int i = 0;
            while (i < _sent.Count && !Echoes(_sent[i], angle))
            {
                i++;
            }

            if (i == _sent.Count)
            {
                _sent.Clear();
                return false;
            }

            _sent.RemoveRange(0, i + 1);
            return true;
        }

        static bool Echoes(float sent, float angle) =>
            Math.Abs(sent - angle) < Match;
    }
}
