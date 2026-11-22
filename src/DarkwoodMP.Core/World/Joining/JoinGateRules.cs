namespace DarkwoodMP.World.Joining
{
    /// The host's decisions about letting a joiner in (WorldShare's join
    /// gate).
    public static class JoinGateRules
    {
        /// A fresh world (a live snapshot) is reused this long.
        public static readonly Seconds SnapshotReuse = new Seconds(20f);

        /// A joiner asking for a world it isn't offered is told again at
        /// most this often.
        public static readonly Seconds StaleTellEvery = new Seconds(2f);

        /// Why a joiner must wait, first that applies; None when it needn't
        /// (and when the host isn't in a world, which Admit refuses). In the
        /// prologue the checks after it are skipped.
        public static JoinWait NotReady(in ReadyFacts facts)
        {
            if (!facts.InWorld)
            {
                return JoinWait.None;
            }

            if (facts.Holding || facts.Film)
            {
                return JoinWait.Intro;
            }

            if (facts.Prologue)
            {
                if (facts.PrologueWaits)
                {
                    return JoinWait.BetweenPrologueParts;
                }

                return JoinWait.None;
            }

            if (facts.Dreaming)
            {
                return JoinWait.Dreaming;
            }

            if (facts.Away)
            {
                return JoinWait.Away;
            }

            if (!facts.WorldReady)
            {
                return JoinWait.Loading;
            }

            return JoinWait.None;
        }

        /// The map test's hold at one ask (HostNotReady): it starts at the
        /// first ask, holds while less than Secs have passed, and then
        /// turns itself off for good. From is never reset, so a second hold
        /// in one run ends at once.
        public static HoldStep Hold(TestHold hold, Seconds now)
        {
            if (!(hold.Secs > 0f))
            {
                return new HoldStep { Hold = hold };
            }

            if (hold.From.Value < 0f)
            {
                hold = hold with { From = now };
            }

            if ((now - hold.From).Value < hold.Secs)
            {
                return new HoldStep { Holding = true, Hold = hold };
            }

            return new HoldStep { Hold = hold with { Secs = 0f } };
        }

        /// The answer to a Hello, first that applies: the ending refuses, a
        /// wait waits, the offered world lets in, no fresh world refuses
        /// out of a world or prepares one, the fresh world lets in when
        /// the joiner has it, else it is offered.
        public static Admission Admit(in AdmitFacts facts)
        {
            if (facts.Ending)
            {
                return Refuse(Refusal.PlayingEnding);
            }

            if (facts.Wait != JoinWait.None)
            {
                return new Admission
                {
                    Step = AdmitStep.Wait,
                    Wait = facts.Wait,
                };
            }

            if (facts.Offered)
            {
                return new Admission { Step = AdmitStep.LetInOffered };
            }

            if (!facts.Fresh)
            {
                if (!facts.InWorld)
                {
                    return Refuse(Refusal.NoSavedGame);
                }

                return new Admission
                {
                    Step = AdmitStep.Prepare,
                    Wait = JoinWait.Preparing,
                };
            }

            if (facts.FreshMatches)
            {
                return new Admission { Step = AdmitStep.LetIn };
            }

            return new Admission { Step = AdmitStep.Offer };
        }

        /// The last world made is still fresh (FreshHostWorld): made in this
        /// scene, and with LiveWorld on, less than SnapshotReuse ago; off,
        /// while the save files' stamp is the same.
        public static bool Fresh(in FreshFacts facts)
        {
            if (!facts.Made || facts.Scene != facts.MadeScene)
            {
                return false;
            }

            if (facts.LiveWorld)
            {
                return facts.Now - facts.MadeAt < SnapshotReuse;
            }

            return facts.Saves && facts.Stamp == facts.MadeStamp;
        }

        /// A joiner that asks for a piece of a world it isn't offered is
        /// told so (OnNeedSave): the first time, or more than StaleTellEvery
        /// after the last.
        public static bool TellsStale(bool told, Seconds at, Seconds now) =>
            !told || now - at > StaleTellEvery;

        /// The host's world changed under its players (Update): a new scene
        /// handle once the world is ready, with a handle before it (0: none)
        /// and somebody in. The handle is tracked with nobody in too.
        public static bool Reloads(int last, int scene, int peers) =>
            last != 0 && scene != last && peers > 0;

        static Admission Refuse(Refusal why) =>
            new Admission { Step = AdmitStep.Refuse, Refusal = why };
    }
}
