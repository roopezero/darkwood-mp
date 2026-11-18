using System;

namespace DarkwoodMP
{
    /// A rule's answer: allowed, or refused with a named reason.
    public readonly struct Verdict : IEquatable<Verdict>
    {
        public static Verdict Allow => default;

        public Refusal Reason { get; }

        /// A value for the refusal's text; null when it has none.
        public string Detail { get; }

        Verdict(Refusal reason, string detail)
        {
            Reason = reason;
            Detail = detail;
        }

        public bool Refused => Reason != Refusal.None;

        public static Verdict Refuse(Refusal reason) =>
            new Verdict(reason, null);

        public static Verdict Refuse(Refusal reason, string detail) =>
            new Verdict(reason, detail);

        public bool Equals(Verdict other) =>
            Reason == other.Reason && Detail == other.Detail;

        public override bool Equals(object obj) =>
            obj is Verdict other && Equals(other);

        public override int GetHashCode() => (int)Reason;

        public override string ToString()
        {
            if (!Refused)
            {
                return "Allow";
            }
            if (Detail == null)
            {
                return "Refuse(" + Reason + ")";
            }
            return "Refuse(" + Reason + ", " + Detail + ")";
        }
    }
}
