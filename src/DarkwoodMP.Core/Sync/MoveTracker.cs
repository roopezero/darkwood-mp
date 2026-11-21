using System.Collections.Generic;

namespace DarkwoodMP.Sync
{
    /// The host's memory of each client's movement and of the trips it
    /// agreed to; the plugin's MoveCheck owns one. MoveRules decides; this
    /// keeps what it decided, per player.
    public sealed class MoveTracker
    {
        readonly Dictionary<ulong, PlayerMovement> _tracks =
            new Dictionary<ulong, PlayerMovement>();

        readonly Dictionary<ulong, List<Trip>> _trips =
            new Dictionary<ulong, List<Trip>>();

        /// A client's player says it is at reported, at now
        /// (Time.realtimeSinceStartup).
        public MoveStep Check(ulong from, Vec3 reported, Seconds now)
        {
            bool known = _tracks.TryGetValue(from, out var was);
            var move = new MoveFacts
            {
                Known = known,
                Was = was,
                Reported = reported,
                Now = now,
            };

            if (MoveRules.AsksForTrip(move))
            {
                bool landed = Landed(from, reported.Ground, now);
                move = move with { TripLanded = landed };
            }

            var step = MoveRules.Check(move);
            _tracks[from] = step.Next;
            return step;
        }

        /// Where this player last was, as the check let them be.
        public bool TryAccepted(ulong from, out Vec3 pos)
        {
            bool known = _tracks.TryGetValue(from, out var was);
            pos = default;
            if (known)
            {
                pos = was.Accepted;
            }

            return known;
        }

        /// Stop tracking this player: the next position they send is taken
        /// as it is. Their trips stay.
        public void Forget(ulong from)
        {
            _tracks.Remove(from);
        }

        /// The player left: their track and their trips go.
        public void Leave(ulong from)
        {
            _tracks.Remove(from);
            _trips.Remove(from);
        }

        /// The session ended.
        public void Clear()
        {
            _tracks.Clear();
            _trips.Clear();
        }

        /// An event the host agreed to will take this player to `to`
        /// before until. Several trips may wait.
        public void ExpectTrip(ulong from, Vec2 to, Seconds until)
        {
            if (!_trips.TryGetValue(from, out var list))
            {
                list = new List<Trip>();
                _trips[from] = list;
            }

            list.Add(new Trip(to, until));
        }

        /// Whether pos is where a trip lands, and that trip is used up.
        /// Trips that ended before now go first; one ending at now is live.
        bool Landed(ulong from, Vec2 pos, Seconds now)
        {
            if (!_trips.TryGetValue(from, out var list))
            {
                return false;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Until < now)
                {
                    list.RemoveAt(i);
                }
            }

            int found = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (Vec2.Distance(pos, list[i].To) < MoveRules.TripLanding)
                {
                    found = i;
                    break;
                }
            }

            if (list.Count == 0)
            {
                _trips.Remove(from);
            }

            if (found < 0)
            {
                return false;
            }

            list.RemoveAt(found);
            if (list.Count == 0)
            {
                _trips.Remove(from);
            }

            return true;
        }

        readonly struct Trip
        {
            public readonly Vec2 To;
            public readonly Seconds Until;

            public Trip(Vec2 to, Seconds until)
            {
                To = to;
                Until = until;
            }
        }
    }
}
