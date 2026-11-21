using System.Collections.Generic;

namespace DarkwoodMP.World.Places
{
    /// Where each location stands (the plugin's Locations._slots, _gaveUp
    /// and _asked). Host: what it promised or used; client: what the host
    /// said, the names asked for and not answered yet, and the names it
    /// gave up on (loaded where its own game puts them).
    public sealed class LocationSpots
    {
        readonly Dictionary<string, Spot> _spots =
            new Dictionary<string, Spot>();

        readonly HashSet<string> _gaveUp = new HashSet<string>();

        readonly HashSet<string> _asked = new HashSet<string>();

        /// The session ended.
        public void Clear()
        {
            _spots.Clear();
            _gaveUp.Clear();
            _asked.Clear();
        }

        /// The spot known for this location, if any.
        public bool TryGet(string name, out Spot spot)
        {
            return _spots.TryGetValue(name, out spot);
        }

        /// Host: remember a spot promised or used.
        public void Promise(string name, Spot spot)
        {
            _spots[name] = spot;
        }

        /// Client: the host said where it stands (LocationSlot). A late
        /// answer doesn't undo a give-up.
        public void Answered(string name, Spot spot)
        {
            _spots[name] = spot;
            _asked.Remove(name);
        }

        /// Client: whether to ask the host (LocationAsk) now: no spot
        /// known, and not asked before in this session. A name given up
        /// is never asked again.
        public bool Ask(string name)
        {
            if (_spots.ContainsKey(name))
            {
                return false;
            }

            return _asked.Add(name);
        }

        /// Client: whether spawnLocation(name) waits for the host first.
        /// It doesn't look at the locations loaded already.
        public bool MustWait(in WaitFacts wait, string name)
        {
            if (!wait.IsClient || !wait.Connected || !wait.InWorld)
            {
                return false;
            }

            return !_spots.ContainsKey(name) && !_gaveUp.Contains(name);
        }

        /// Client: the host didn't answer in time.
        public void GiveUp(string name)
        {
            _gaveUp.Add(name);
        }

        /// Client: whether we gave up on this location, by its name or its
        /// true name (Core.getTrueLocationName).
        public bool GaveUpOn(string name, string trueName)
        {
            return _gaveUp.Contains(name) || _gaveUp.Contains(trueName);
        }

        /// In spawnLocation: the spot to use instead of the game's pick,
        /// with an active session and a name.
        public bool TrySpotFor(bool active, string name, out Spot spot)
        {
            spot = default;
            if (!active || name == null)
            {
                return false;
            }

            return _spots.TryGetValue(name, out spot);
        }

        /// In LocationMarker.spawnLocation: the turn to use. The shell
        /// rolls the game's turn first, always, so the game's dice advance
        /// even when the host's turn is used.
        public int TurnFor(bool active, string name, int rolled)
        {
            if (!active || name == null)
            {
                return rolled;
            }

            if (!_spots.TryGetValue(name, out var spot))
            {
                return rolled;
            }

            return spot.Turn;
        }
    }
}
