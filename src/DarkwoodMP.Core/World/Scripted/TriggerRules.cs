namespace DarkwoodMP.World.Scripted
{
    /// What the host checks of an event trigger a client asks about (the
    /// plugin's ScriptedEvents.OnPlayerPart, TriggerAllowed and
    /// ExitAllowed), and whether a trigger runs once.
    public static class TriggerRules
    {
        /// How far from a trigger a player may be to set it off, ask for
        /// their part of it or leave it.
        public const float Reach = 3000f;

        /// Whether the trigger runs its events only once: it fires once, or
        /// it fires each time but its events run once.
        public static bool Once(in TriggerFacts t)
        {
            return !t.MultipleFire || (t.HasEvents && !t.EventsMultipleFire);
        }

        /// Whether a once-only trigger has had its one run.
        public static bool OnceDone(in TriggerFacts t)
        {
            if (!t.MultipleFire)
            {
                return t.Fired;
            }

            return t.HasEvents && !t.EventsMultipleFire && t.EventsFired;
        }

        /// Whether something that happens once has happened (a trigger, its
        /// events or a world event: fired, and not each time).
        public static bool Spent(bool fired, bool multiple)
        {
            return fired && !multiple;
        }

        /// Whether our player is still owed its part of this trigger: found,
        /// once-only, done, with steps a later player gets, and owed by
        /// the record (OwedParts.Owed).
        public static bool IsOwed(in TriggerFacts t, bool owed)
        {
            return t.Exists && Once(t) && OnceDone(t) && t.LaterSteps
                && owed;
        }

        /// Host, kind 5: a client asks for its own part of a once-only
        /// trigger that already fired. The first check that fails wins.
        public static Verdict MayGivePart(in TriggerFacts t)
        {
            if (!t.Exists)
            {
                return Verdict.Refuse(Refusal.NoSuchTrigger);
            }

            if (t.Disabled)
            {
                return Verdict.Refuse(Refusal.EventSwitchedOff);
            }

            if (!Once(t) || !OnceDone(t))
            {
                return Verdict.Refuse(Refusal.NotHappened);
            }

            if (!t.LaterSteps)
            {
                return Verdict.Refuse(Refusal.NoPlayerPart);
            }

            if (t.Given)
            {
                return Verdict.Refuse(Refusal.HadTheirPart);
            }

            return InReachAndMet(t);
        }

        /// Host, kind 1 with the trigger found: it can fire in the host's
        /// world and the requester is near it. The first check that fails
        /// wins.
        public static Verdict MayFire(in TriggerFacts t)
        {
            if (t.Disabled)
            {
                return Verdict.Refuse(Refusal.EventSwitchedOff);
            }

            if (Spent(t.Fired, t.MultipleFire))
            {
                return Verdict.Refuse(Refusal.AlreadyHappened);
            }

            if (!t.HasEvents)
            {
                return Verdict.Refuse(Refusal.NothingHappens);
            }

            if (Spent(t.EventsFired, t.EventsMultipleFire))
            {
                return Verdict.Refuse(Refusal.EventsRan);
            }

            return InReachAndMet(t);
        }

        /// Host, kind 4: a client's player left this area trigger. A
        /// trigger the host hasn't got isn't checked (it is passed on).
        public static Verdict MayExit(in TriggerFacts t)
        {
            if (!t.Exists)
            {
                return Verdict.Allow;
            }

            if (!t.Area)
            {
                return Verdict.Refuse(Refusal.NotAnArea, t.Name);
            }

            return InReach(t);
        }

        /// The requester's distance from the trigger, at the reach.
        static Verdict InReach(in TriggerFacts t)
        {
            return ReachRules.InReach(t.Reach with { Reach = Reach });
        }

        /// The reach, then the requirements the host checks.
        static Verdict InReachAndMet(in TriggerFacts t)
        {
            var reach = InReach(t);
            if (reach.Refused)
            {
                return reach;
            }

            if (t.Unmet != null)
            {
                return Verdict.Refuse(Refusal.RequirementNotMet, t.Unmet);
            }

            return Verdict.Allow;
        }
    }
}
