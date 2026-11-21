using System;

namespace DarkwoodMP.Menus
{
    /// What leaving a world loses, said in the question before it
    /// (MenuList.LeaveText).
    public static class LeaveRules
    {
        /// The words after "Leave this world for the main menu? " or
        /// "Quit Darkwood? ": what is lost, then who else it affects. A
        /// host with players in the game is told first, so a host is
        /// never told they leave the host's game.
        public static string Text(in LeaveFacts leave)
        {
            string text;
            if (leave.HostCopy)
            {
                text = "This is your copy of the host's world: your own saves"
                    + " are untouched.";
            }
            else if (!leave.SaveKnown)
            {
                text = "What you did since the last save will be lost.";
            }
            else
            {
                text = "What you did since the last save ("
                    + Ago(leave.Now - leave.Saved) + " ago) will be lost.";
            }

            if (leave.Host && leave.Peers > 0)
            {
                text += " The players in your game go back to their menus.";
            }
            else if (leave.Client)
            {
                text += " You leave the host's game.";
            }

            return text;
        }

        /// How long ago, as the question says it: "{h} h {m} min" from an
        /// hour, "{m} min" from a minute, else "{s} s". The hours and
        /// minutes are cut, not rounded, and the seconds are TimeSpan's
        /// seconds part. A save in the future (the clock went back) says
        /// "0 s", however far ahead it is.
        public static string Ago(TimeSpan since)
        {
            if (since.TotalHours >= 1)
            {
                return $"{(int)since.TotalHours} h {since.Minutes} min";
            }

            if (since.TotalMinutes >= 1)
            {
                return $"{(int)since.TotalMinutes} min";
            }

            return $"{Math.Max(0, since.Seconds)} s";
        }
    }
}
