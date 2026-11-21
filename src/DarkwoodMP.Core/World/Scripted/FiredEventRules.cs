namespace DarkwoodMP.World.Scripted
{
    /// What a game does with an EventFired from another game, and what a
    /// client does with the host's answer to its own (the plugin's
    /// ScriptedEvents.OnFired, ExitArrived, Send and LaterPart).
    public static class FiredEventRules
    {
        /// How near the trigger a client's player must be to see the
        /// messages of an event the host's world set off. In 3D.
        public const float NearMessages = 2000f;

        /// The host's result for a request that is all the client's.
        public const string Alone = "alone";

        /// The words of a reach refusal (ReachRules.InReach) a client
        /// looks for: too far away is asked again next time.
        public const string ReachWords = "(reach";

        /// Host: the world's own triggers come only from the host, so a
        /// client's kind 3 or 6 is refused (outside a request, so nothing
        /// is sent back).
        public static Verdict HostTakes(EventKind kind)
        {
            if (kind == EventKind.WorldTrigger
                || kind == EventKind.WorldTriggerCaused)
            {
                return Verdict.Refuse(Refusal.HostRunsWorldEvents);
            }

            return Verdict.Allow;
        }

        /// Host, kind 1: a client set off a trigger. One the host hasn't
        /// got is all theirs when they are away, else allowed only if the
        /// host has every world event it fires. A trigger the host has is
        /// checked by TriggerRules.MayFire.
        public static EventAnswer AnswerTrigger(
            in TriggerFacts t,
            bool away,
            bool known)
        {
            if (!t.Exists && away)
            {
                return new EventAnswer { Alone = true };
            }

            if (!t.Exists && !known)
            {
                var refused = Verdict.Refuse(Refusal.NoSuchEvent);
                return new EventAnswer { Verdict = refused };
            }

            if (t.Exists)
            {
                return new EventAnswer { Verdict = TriggerRules.MayFire(t) };
            }

            return default;
        }

        /// Host, kind 2: a client fired a world event by name. One the
        /// host hasn't got is all theirs when they are away.
        public static EventAnswer AnswerWorldEvent(
            in EventsFacts events,
            string name,
            bool away)
        {
            if (!events.Exists && away)
            {
                return new EventAnswer { Alone = true };
            }

            if (!events.Exists)
            {
                var unknown = Verdict.Refuse(Refusal.NoSuchWorldEvent, name);
                return new EventAnswer { Verdict = unknown };
            }

            if (TriggerRules.Spent(events.Fired, events.MultipleFire))
            {
                var spent = Verdict.Refuse(Refusal.WorldEventHappened, name);
                return new EventAnswer { Verdict = spent };
            }

            return default;
        }

        /// Client, kind 3 or 6: the part a world trigger runs in here.
        /// The client who caused it runs its own part; one near it sees
        /// the messages.
        public static EventPart WorldTriggerPart(bool ours, bool near)
        {
            if (ours)
            {
                return EventPart.PlayerAndShared;
            }

            if (near)
            {
                return EventPart.SharedAndMessages;
            }

            return EventPart.SharedOnly;
        }

        /// Client: whether our player is near enough a world trigger to see
        /// its messages: closer than NearMessages, in 3D. False with no
        /// player.
        public static bool NearTrigger(bool known, Vec3 player, Vec3 at)
        {
            if (!known)
            {
                return false;
            }

            float x = player.X - at.X;
            float y = player.Y - at.Y;
            float z = player.Z - at.Z;
            return x * x + y * y + z * z < NearMessages * NearMessages;
        }

        /// Whether events may run now: there are some, not run yet (or
        /// run each time), and active or allowed to run inactive.
        public static bool CanRun(in EventsFacts events)
        {
            if (!events.Exists)
            {
                return false;
            }

            if (TriggerRules.Spent(events.Fired, events.MultipleFire))
            {
                return false;
            }

            return events.Active || events.RunsInactive;
        }

        /// Another player left an area trigger (kind 4): run its exit once
        /// per entry, and not while our own player is still in it.
        public static ExitArrival OnExit(in TriggerFacts t, bool inside)
        {
            if (!t.Exists || t.Disabled || !t.Area)
            {
                return ExitArrival.Ignored;
            }

            if (t.FiredExit)
            {
                return ExitArrival.RanAlready;
            }

            if (inside)
            {
                return ExitArrival.StillInside;
            }

            if (t.MultipleFire)
            {
                return ExitArrival.RunAndRearm;
            }

            return ExitArrival.Run;
        }

        /// Whether a player went into an area trigger and hasn't left it
        /// (EventTriggers.entered and exited, as OnTriggerExit counts).
        public static bool Entered(int entered, int exited)
        {
            return entered > exited;
        }

        /// Client: the part our trigger or world event runs in after the
        /// host's yes. All of it when the host says it's ours alone.
        public static EventPart PartAfterYes(string result)
        {
            if (result == Alone)
            {
                return EventPart.All;
            }

            return EventPart.PlayerAndShared;
        }

        /// Client: whether to ask again for our part of a trigger the host
        /// refused. Only a reach refusal is asked again; any other (also
        /// none, or "the session ended") marks the part had.
        public static bool AskAgain(string reason)
        {
            return reason != null && reason.Contains(ReachWords);
        }
    }
}
