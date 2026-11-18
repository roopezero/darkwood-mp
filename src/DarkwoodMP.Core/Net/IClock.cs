namespace DarkwoodMP.Net
{
    /// The two clocks the session uses; the plugin reads Unity's. They are
    /// different clocks and must stay apart.
    public interface IClock
    {
        /// Time.realtimeSinceStartup.
        float RealTime { get; }

        /// Time.unscaledTime.
        float UnscaledTime { get; }
    }
}
