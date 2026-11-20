namespace DarkwoodMP.World.Dragging
{
    /// Loose things pushed by anything (a player, an enemy, physics), from
    /// the plugin's DragSync: the host's resting place is the last word on
    /// each. The shell finds the bodies (FindObjectsOfType,
    /// OverlapSphereNonAlloc) and keeps where each is known to be.
    public static class PushRules
    {
        /// How far around a player new loose things are looked for.
        public const float WatchRadius = 600f;

        /// The most colliders one look takes (DragSync._near).
        public const int LookMost = 512;

        /// Seconds between two push checks; also how often a thing still
        /// sliding is sent.
        public const float WatchEvery = 0.1f;

        /// Seconds between two looks for new loose things.
        public const float LookEvery = 0.25f;

        /// Units: moved less than this (and turned less than MinTurn) is
        /// where it is known to be. A crate is about 50 across.
        public const float MinMove = 0.5f;

        /// Degrees.
        public const float MinTurn = 2f;

        /// A client's player this close to a thing's collider moved it.
        public const float Touch = 120f;

        /// Seconds: still moving after this, it is taken as it is.
        public const float GiveUpAfter = 10f;

        /// Seconds a move of a thing our player hit counts as our push (a
        /// shot pushes from afar).
        public const float HitFor = 3f;

        /// At rest under this squared velocity...
        public const float RestSpeed = 1f;

        /// ...and this squared angular velocity, or asleep.
        public const float RestSpin = 0.01f;

        /// Whether a body is a loose thing: free to move, an Item, not a
        /// character, and not part of a door or a window.
        public static bool IsLoose(in LooseFacts body)
        {
            if (!body.Body || body.Frozen || !body.Item)
            {
                return false;
            }

            return !body.Character && !body.InDoor && !body.InWindow;
        }

        /// Whether a body is at rest: asleep, or both its squared velocity
        /// and squared angular velocity under their limits.
        public static bool AtRest(bool asleep, float speed, float spin)
        {
            if (asleep)
            {
                return true;
            }

            return speed < RestSpeed && spin < RestSpin;
        }

        /// Whether a thing is away from a place: moved MinMove or more, or
        /// turned MinTurn or more.
        public static bool Away(float moved, float turned)
        {
            return !(moved < MinMove && turned < MinTurn);
        }

        /// One push check of a watched thing. A client sends only what its
        /// own player pushed (at it, or hit it lately) and puts the rest
        /// back for the host's word; in an area the host doesn't run
        /// nothing is touched, so everything that moves there is put back,
        /// our player's pushes too.
        public static PushStep Watch(in PushFacts push)
        {
            if (push.Gone)
            {
                // The shell removes the entry, so no state goes on.
                return new PushStep { Outcome = PushOutcome.Drop };
            }

            if (push.Kinematic || push.Dragged || push.Settling
                || push.Shown)
            {
                return new PushStep { Outcome = PushOutcome.Rebase };
            }

            if (!Away(push.Moved, push.Turned))
            {
                return new PushStep { Outcome = PushOutcome.Still };
            }

            var since = push.Since;
            if (!push.Strayed)
            {
                since = push.Now;
            }

            bool touched = push.Touched || Touches(push);
            if (!push.AtRest && Ago(since, push.Now) < GiveUpAfter)
            {
                var outcome = PushOutcome.Wait;
                if (push.Host || touched)
                {
                    outcome = PushOutcome.Slide;
                }

                return new PushStep
                {
                    Outcome = outcome,
                    Strayed = true,
                    Since = since,
                    Touched = touched,
                };
            }

            if (!push.Host && !touched)
            {
                return new PushStep { Outcome = PushOutcome.PutBack };
            }

            return new PushStep { Outcome = PushOutcome.Settle };
        }

        /// Client: whether our copy of a thing just tied goes where the
        /// host has it: loose, not held by anything, MinMove or more away,
        /// and nobody else drags it. Only its place is set, not its turn.
        public static bool PutsAtHost(in PlaceFacts place)
        {
            if (!place.Client || !place.Loose || place.Kinematic)
            {
                return false;
            }

            if (place.Dragged || place.Settling || place.Shown)
            {
                return false;
            }

            if (place.Distance < MinMove)
            {
                return false;
            }

            return place.Other == 0;
        }

        /// Host, a joiner came into the world: what is sent for one thing
        /// that moved since its snapshot. A thing settling after our drag
        /// is not skipped.
        public static JoinerPlace Joiner(in SnapshotFacts thing)
        {
            if (thing.Gone || !Away(thing.Moved, thing.Turned))
            {
                return JoinerPlace.Skip;
            }

            if (!thing.Loose || thing.Kinematic || thing.Dragged
                || thing.Shown)
            {
                return JoinerPlace.Skip;
            }

            if (thing.AtRest)
            {
                return JoinerPlace.Send;
            }

            if (thing.Watched)
            {
                return JoinerPlace.Later;
            }

            return JoinerPlace.Skip;
        }

        /// A client's own player pushed it: at it, or hit it under HitFor
        /// ago. Never on the host, nor in an area the host doesn't run.
        static bool Touches(in PushFacts push)
        {
            if (push.Host || !push.HostRunsHere)
            {
                return false;
            }

            if (push.Collider && push.Reach < Touch * Touch)
            {
                return true;
            }

            return push.Hit && Ago(push.HitAt, push.Now) < HitFor;
        }

        static float Ago(Seconds then, Seconds now)
        {
            return now.Value - then.Value;
        }
    }
}
