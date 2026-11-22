namespace DarkwoodMP
{
    /// A time or a span of time.
    public readonly struct Seconds
    {
        public readonly float Value;

        public Seconds(float value)
        {
            Value = value;
        }

        public static Seconds operator -(Seconds a, Seconds b) =>
            new Seconds(a.Value - b.Value);

        public static bool operator >(Seconds a, Seconds b) =>
            a.Value > b.Value;

        public static bool operator <(Seconds a, Seconds b) =>
            a.Value < b.Value;
    }
}
