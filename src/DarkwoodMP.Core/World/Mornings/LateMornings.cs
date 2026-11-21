using System.Collections.Generic;

namespace DarkwoodMP.World.Mornings
{
    /// Host: the day whose morning is over, and when each joiner still in
    /// a morning was last told so (the plugin's AfterNight._overDay and
    /// _told). A joiner whose morning starts after the first player walked
    /// out is told it is over, every few seconds, until the next dawn.
    public sealed class LateMornings
    {
        /// OverDay when no morning is over.
        public const int None = -1;

        readonly Dictionary<ulong, Seconds> _told =
            new Dictionary<ulong, Seconds>();

        /// The day whose morning is over, or None.
        public int OverDay { get; private set; } = None;

        /// The session ended: nothing is over.
        public void Clear()
        {
            OverDay = None;
            _told.Clear();
        }

        /// A new dawn in the host's game (startAfterNight): today's
        /// morning isn't over yet. The shell calls it on the host only.
        public void Dawn()
        {
            OverDay = None;
            _told.Clear();
        }

        /// The morning of this day is over (the host's ended, or a joiner
        /// walked out).
        public void Over(int day)
        {
            OverDay = day;
        }

        /// Whether to look at the joiners this frame. A new day forgets the
        /// old one's morning (and whom we told) and looks at nobody.
        public bool Check(in LateFacts late)
        {
            if (OverDay < 0 || !late.HasSync || !late.InWorld)
            {
                return false;
            }

            if (OverDay != late.Today)
            {
                OverDay = None;
                _told.Clear();
                return false;
            }

            return true;
        }

        /// Whether to send this joiner AfterNightEnded now; if so, now is
        /// remembered as when they were told.
        public bool Tell(ulong peer, in RemoteMorning remote, Seconds now)
        {
            if (!remote.HasState || !remote.InMorning || !remote.IsPeer)
            {
                return false;
            }

            if (_told.TryGetValue(peer, out var at)
                && now.Value - at.Value < MorningRules.TellEvery)
            {
                return false;
            }

            _told[peer] = now;
            return true;
        }
    }
}
