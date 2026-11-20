namespace DarkwoodMP.Sync
{
    /// The host's check on where a client says its player is (the plugin's
    /// MoveCheck.Check). Over each half second the player may not go faster
    /// than MaxSpeed; a jump further than Teleport, or to where a trip the
    /// host agreed to lands, starts the check again there. Memory goes in
    /// and comes back out: MoveTracker keeps it per player.
    public static class MoveRules
    {
        /// Units a second; running is about a quarter of this.
        public const float MaxSpeed = 2000f;

        /// Further than this in one update is the game moving the player.
        public const float Teleport = 3000f;

        /// How near an agreed trip's destination the player must land.
        public const float TripLanding = 400f;

        /// Seconds over which speed is measured, and the least time
        /// between two corrections.
        public const float Window = 0.5f;

        /// When a new track was last put back: long enough ago that its
        /// first correction is sent.
        public const float NeverPutBack = -10f;

        /// Whether the trips the host agreed to are asked about: for a
        /// known player who went further than TripLanding, but not further
        /// than Teleport. Only then is a trip used up.
        public static bool AsksForTrip(in MoveFacts move)
        {
            if (!move.Known)
            {
                return false;
            }

            float gone = Gone(move);
            if (gone > Teleport)
            {
                return false;
            }

            return gone > TripLanding;
        }

        /// Where the player may be, and what the host remembers next.
        public static MoveStep Check(in MoveFacts move)
        {
            if (!move.Known)
            {
                return StartAgain(move, MoveOutcome.Started);
            }

            float gone = Gone(move);
            if (gone > Teleport)
            {
                return StartAgain(move, MoveOutcome.Started);
            }

            if (gone > TripLanding && move.TripLanded)
            {
                return StartAgain(move, MoveOutcome.Tripped);
            }

            var since = move.Now - move.Was.MeasureAt;

            // As Mathf.Max(since, Window).
            float span = Window;
            if (since.Value > Window)
            {
                span = since.Value;
            }

            float measured = Vec2.Distance(
                move.Reported.Ground,
                move.Was.MeasureFrom);
            float speed = measured / span;
            if (speed > MaxSpeed)
            {
                return PutBack(move, speed);
            }

            return Accept(move, since);
        }

        static float Gone(in MoveFacts move) =>
            Vec2.Distance(move.Reported.Ground, move.Was.Accepted.Ground);

        static MoveStep StartAgain(in MoveFacts move, MoveOutcome outcome)
        {
            var next = new PlayerMovement
            {
                Accepted = move.Reported,
                MeasureFrom = move.Reported.Ground,
                MeasureAt = move.Now,
                PutBackAt = new Seconds(NeverPutBack),
            };

            return new MoveStep
            {
                Next = next,
                Accepted = move.Reported,
                Outcome = outcome,
            };
        }

        static MoveStep PutBack(in MoveFacts move, float speed)
        {
            var was = move.Was;
            bool send = (move.Now - was.PutBackAt).Value > Window;
            var putBackAt = was.PutBackAt;
            if (send)
            {
                putBackAt = move.Now;
            }

            var next = new PlayerMovement
            {
                Accepted = was.Accepted,
                MeasureFrom = was.Accepted.Ground,
                MeasureAt = move.Now,
                PutBackAt = putBackAt,
            };

            return new MoveStep
            {
                Next = next,
                Accepted = was.Accepted,
                Outcome = MoveOutcome.PutBack,
                SendCorrection = send,
                Speed = speed,
            };
        }

        static MoveStep Accept(in MoveFacts move, Seconds since)
        {
            var was = move.Was;
            var from = was.MeasureFrom;
            var at = was.MeasureAt;
            if (since.Value >= Window)
            {
                from = move.Reported.Ground;
                at = move.Now;
            }

            var next = new PlayerMovement
            {
                Accepted = move.Reported,
                MeasureFrom = from,
                MeasureAt = at,
                PutBackAt = was.PutBackAt,
            };

            return new MoveStep { Next = next, Accepted = move.Reported };
        }
    }
}
