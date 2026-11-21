namespace DarkwoodMP.World.Mornings
{
    /// The morning after a night (the plugin's AfterNight). The host's
    /// clock is everyone's, so the morning ends for everyone when the
    /// first player walks out of their hideout, and the host's clock runs
    /// during its own morning while a joiner is out.
    public static class MorningRules
    {
        /// Seconds after the host's morning froze before a joiner out of
        /// theirs frees the clock: a joiner's dawn comes a moment later.
        public const float Grace = 5f;

        /// Seconds between two tellings of a late joiner.
        public const float TellEvery = 3f;

        /// Seconds a waiting end tries to close the dialogue before it
        /// closes it without the NPC.
        public const float CloseTimeout = 10f;

        /// The host's clock this frame. Not frozen: forget the state, and
        /// leave the game's clock as it is, even if we freed it.
        public static ClockStep Clock(in ClockFacts clock, ClockState old)
        {
            if (!clock.Frozen)
            {
                return default;
            }

            var since = old.Since;
            if (!old.Timing)
            {
                since = clock.Now;
            }

            bool late = clock.Now.Value - since.Value >= Grace;
            bool someoneOut = clock.InCoop && clock.HasSync && late
                && clock.AnyoneOut;
            var change = ClockChange.None;
            bool freed = old.Freed;
            if (someoneOut && !old.Freed)
            {
                freed = true;
                change = ClockChange.Run;
            }
            else if (!someoneOut && old.Freed)
            {
                freed = false;
                change = ClockChange.Stop;
            }

            var state = new ClockState
            {
                Freed = freed,
                Timing = true,
                Since = since,
            };

            return new ClockStep { Clock = state, Change = change };
        }

        /// Whether a remote player counts as out of their morning: not the
        /// test dummy, with a PlayerState that says they aren't in one.
        public static bool IsOut(bool dummy, bool hasState, bool inMorning)
        {
            return !dummy && hasState && !inMorning;
        }

        /// Whether Controller.endAfterNight ended the morning in a way the
        /// host sends. Killing the trader doesn't: NpcDied ends it in
        /// every game.
        public static bool EndedHere(bool before, bool after, bool killed)
        {
            return before && !after && !killed;
        }

        /// The host's own morning ended (EndedHere): remember the day, and
        /// tell everyone when anyone is in.
        public static HostEndStep HostEnded(in HostEndFacts end)
        {
            if (!end.IsHost || !end.InWorld || end.Replaying)
            {
                return default;
            }

            return new HostEndStep
            {
                MarkOver = end.HasController,
                Send = end.Peers > 0,
            };
        }

        /// Whether a client sends AfterNightLeft: any trigger exit that
        /// ended its morning, its own doing.
        public static bool WalkedOut(in WalkOutFacts walk)
        {
            if (!walk.Before || !walk.HasController || walk.After)
            {
                return false;
            }

            return walk.IsClient && walk.InWorld && !walk.Replaying;
        }

        /// The host gets AfterNightLeft. The day is over before it looks at
        /// its own morning.
        public static LeftStep OnLeft(in LeftFacts left)
        {
            if (!left.IsHost || !left.FromPeer || !left.InWorld)
            {
                return LeftStep.Ignore;
            }

            if (!left.HasController)
            {
                return LeftStep.Ignore;
            }

            if (!left.InMorning || left.Dreaming)
            {
                return LeftStep.Relay;
            }

            if (left.Talking)
            {
                return LeftStep.EndAfterTalk;
            }

            return LeftStep.End;
        }

        /// A client gets AfterNightEnded. One that comes while an end is
        /// waiting is ignored.
        public static EndedStep OnEnded(in EndedFacts ended)
        {
            if (!ended.IsClient || !ended.FromHost || !ended.InWorld)
            {
                return EndedStep.Ignore;
            }

            if (ended.Pending)
            {
                return EndedStep.Ignore;
            }

            if (!ended.HasController || !ended.InMorning || ended.Dreaming)
            {
                return EndedStep.AlreadyOver;
            }

            if (ended.Talking)
            {
                return EndedStep.EndAfterTalk;
            }

            return EndedStep.End;
        }

        /// Whether ending our morning for another game's is a replay. On
        /// the host it isn't, so its end is sent to everyone.
        public static bool EndsAsReplay(bool host)
        {
            return !host;
        }

        /// Whether our dialogue is with a hideout's morning visitor: its
        /// trader (which endAfterNight destroys) or the wolf.
        public static bool Talking(in VisitorFacts visitor)
        {
            if (!visitor.HasPlayer || !visitor.HasWindow)
            {
                return false;
            }

            if (!visitor.HasNpc || !visitor.InDialogue)
            {
                return false;
            }

            return visitor.HasLocations && visitor.AtBase;
        }

        /// A waiting end, each frame, before it closes the dialogue.
        public static PendingStep Pending(in PendingFacts pending)
        {
            if (!pending.Pending)
            {
                return PendingStep.Nothing;
            }

            if (!pending.HasController || !pending.HasWindow)
            {
                return PendingStep.Forget;
            }

            if (!pending.HasPlayer || !pending.InWorld)
            {
                return PendingStep.Forget;
            }

            if (!pending.Talking)
            {
                return PendingStep.End;
            }

            return PendingStep.Close;
        }

        /// A waiting end after it tried to close the dialogue: the NPC let
        /// go, or more than CloseTimeout passed since the end began.
        public static CloseStep AfterClose(
            bool gone,
            Seconds now,
            Seconds since)
        {
            if (gone)
            {
                return CloseStep.End;
            }

            if (now.Value - since.Value > CloseTimeout)
            {
                return CloseStep.ForceEnd;
            }

            return CloseStep.Wait;
        }
    }
}
