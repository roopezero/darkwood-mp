using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// A clock the test moves by hand.
    public sealed class FakeClock : IClock
    {
        public float RealTime { get; set; }

        public float UnscaledTime { get; set; }
    }
}
