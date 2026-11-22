using UnityEngine;

namespace DarkwoodMP.Net
{
    /// The core's clocks, read from Unity's Time.
    public sealed class UnityClock : IClock
    {
        public float RealTime => Time.realtimeSinceStartup;

        public float UnscaledTime => Time.unscaledTime;
    }
}
