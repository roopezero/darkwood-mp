using System;

namespace DarkwoodMP.World.Doors
{
    /// Which open doors' leaves this game watches for pushes, and what it
    /// does with each one every frame (the plugin's DoorPush.Update,
    /// Look and Opened). Times are Time.unscaledTime.
    public static class LeafWatchRules
    {
        /// Open doors this near our player are found and watched.
        public const float WatchRadius = 500f;

        /// A watched leaf further than WatchRadius and this is let go.
        public const float UnwatchMargin = 100f;

        /// Our player this near the leaf, while it moves, pushed it.
        public const float TouchDistance = 120f;

        /// A leaf further than this from where it's shared has moved.
        public const float MinChange = 4f;

        /// A leaf turning faster than this (Rigidbody.angularVelocity.y)
        /// is swinging.
        public const float SwingSpeed = 0.2f;

        /// A leaf still for this long has come to rest.
        public static readonly Seconds RestTime = new Seconds(0.4f);

        /// Where a leaf rests isn't sent more often than this.
        public static readonly Seconds MinInterval = new Seconds(0.5f);

        /// A swinging leaf's angle is sent this often.
        public static readonly Seconds StreamInterval = new Seconds(0.1f);

        /// Open doors near our player are looked for this often.
        public static readonly Seconds LookInterval = new Seconds(1f);

        /// Time to look for open doors near our player again: now has
        /// reached next (the plugin's _nextLook, which then becomes now +
        /// LookInterval).
        public static bool DueToLook(Seconds now, Seconds next) =>
            now.Value >= next.Value;

        /// DoorPush.Look: a door found near our player, through its
        /// leaf's collider, is watched from now at its current angle.
        public static bool Looks(in LeafDoorFacts door)
        {
            return door.Exists
                && !door.Watched
                && door.Opened
                && !door.Destroyed
                && door.HasBody
                && door.HasRigidbody;
        }

        /// What a leaf found by Look starts as: shared where it is.
        public static LeafWatch FromLook(float signed) =>
            new LeafWatch { Shared = signed };

        /// DoorPush.Opened: this game opened the door itself (not a
        /// replay), so it's watched from shut, as our player's push.
        public static bool WatchesOpened(in LeafOpenFacts open)
        {
            var door = open.Door;
            return open.Sharing
                && !open.Replaying
                && door.Exists
                && door.HasBody
                && door.Opened
                && !door.Destroyed
                && door.HasRigidbody;
        }

        /// What a door this game opened starts as: shared shut, moving
        /// and touched.
        public static LeafWatch FromOpen() =>
            new LeafWatch { Shared = 0f, Moving = true, Touched = true };

        /// A watched door that is gone, shut, broken, without its leaf or
        /// Rigidbody, switched off, or too far from our player is let go.
        public static bool Unwatches(in LeafDoorFacts door)
        {
            return !door.Exists
                || !door.Opened
                || door.Destroyed
                || !door.HasBody
                || !door.HasRigidbody
                || !door.Active
                || door.Distance > WatchRadius + UnwatchMargin;
        }

        /// A watched leaf that is kinematic or being glided this frame
        /// is skipped: something else is turning it.
        public static bool Waits(in LeafDoorFacts door) =>
            door.Kinematic || door.Gliding;

        /// The leaf's angle was heard from another game, or a glide put
        /// it there: shared there, and neither moving, touched nor
        /// resting.
        public static LeafWatch Heard(in LeafWatch watch, float angle)
        {
            return watch with
            {
                Shared = angle,
                Moving = false,
                Touched = false,
                Resting = false,
            };
        }

        /// One frame of a watched leaf: follow it while it swings or has
        /// turned, stream it while it swings (the host, or a client whose
        /// player touched it), and once it has rested RestTime (and
        /// MinInterval after the last send) send where it rests, or on a
        /// client its player didn't touch, swing it back.
        public static LeafStep Step(in LeafFrame frame)
        {
            var w = frame.Watch;
            float now = frame.Angle;
            bool swinging = Math.Abs(frame.Spin) > SwingSpeed;
            if (swinging || !w.Moving && Moved(now, w.Shared))
            {
                w = w with { Moving = true };
                if (!w.Touched && Touches(frame))
                {
                    w = w with { Touched = true };
                }
            }

            if (swinging)
            {
                return Swing(frame, w with { Resting = false });
            }

            if (!w.Moving)
            {
                return Idle(w);
            }

            if (!w.Resting)
            {
                var still = frame.Now;
                return Idle(w with { Resting = true, RestingSince = still });
            }

            float rested = (frame.Now - w.RestingSince).Value;
            if (rested < RestTime.Value || frame.Now < w.NextSend)
            {
                return Idle(w);
            }

            return Rest(frame, w with { Moving = false });
        }

        /// A swinging leaf: streamed, if ours to send and due.
        static LeafStep Swing(in LeafFrame frame, LeafWatch w)
        {
            bool ours = frame.Host || w.Touched;
            if (!ours || frame.Now.Value < w.NextStream.Value)
            {
                return Idle(w);
            }

            var next = new Seconds(frame.Now.Value + StreamInterval.Value);
            w = w with { NextStream = next };
            if (!frame.Named)
            {
                return Idle(w);
            }

            return new LeafStep
            {
                Next = w,
                Move = LeafMove.Stream,
                Angle = frame.Angle,
            };
        }

        /// A leaf that has come to rest: sent or swung back if it is away
        /// from where it's shared. Either way it's untouched after.
        static LeafStep Rest(in LeafFrame frame, LeafWatch w)
        {
            float now = frame.Angle;
            if (!Moved(now, w.Shared))
            {
                return Idle(w with { Touched = false });
            }

            if (!frame.Host && !w.Touched)
            {
                return new LeafStep
                {
                    Next = w with { Touched = false },
                    Move = LeafMove.SwingBack,
                    Angle = w.Shared,
                };
            }

            if (!frame.Named)
            {
                return Idle(w with { Touched = false });
            }

            var next = new Seconds(frame.Now.Value + MinInterval.Value);
            var sent = w with { Shared = now, NextSend = next };
            sent = sent with { Touched = false };
            return new LeafStep
            {
                Next = sent,
                Move = LeafMove.Send,
                Angle = now,
            };
        }

        static LeafStep Idle(LeafWatch w) =>
            new LeafStep { Next = w, Move = LeafMove.None };

        static bool Moved(float now, float shared) =>
            Math.Abs(LeafRules.DeltaAngle(now, shared)) > MinChange;

        static bool Touches(in LeafFrame frame)
        {
            float d = LeafRules.TouchDistance(frame.Shape, frame.Player);
            return d < TouchDistance || frame.TestTouch;
        }
    }
}
