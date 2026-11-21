using System;
using System.Collections.Generic;

namespace DarkwoodMP.Sync.Players
{
    /// Soft collision between players: each game eases only its own player
    /// out of the others' puppets, faster the deeper they overlap
    /// (PlayerSeparation). Its body radius isn't SpawnRules'.
    public static class SeparationRules
    {
        /// Units a second with the two right on top of each other (walking
        /// is about 250).
        public const float MaxSpeed = 120f;

        /// The body radius when the colliders give none that is sensible.
        public const float DefaultRadius = 18f;

        /// A measured radius must be above this...
        public const float MinRadius = 1f;

        /// ...and at most this, or DefaultRadius is used.
        public const float MaxRadius = 60f;

        /// Closer than this, two centres count as on top of each other, and
        /// the ids pick the way out.
        public const float Overlap = 0.01f;

        /// The longest frame a step is made for (seconds).
        public const float MaxDelta = 0.05f;

        /// Unity's Vector3 == compares the squared length of the
        /// difference with this.
        const float ZeroSquared = 9.99999944E-11f;

        /// Vector3.normalized gives zero at this length or less.
        const float NormalEpsilon = 1E-05f;

        /// Eased only with the setting on, in a world, with a puppet, our
        /// player there, alive, not dying and free to move, and a
        /// rigidbody that isn't kinematic.
        public static bool Eases(in SeparationFacts frame)
        {
            if (frame.SwitchedOff)
            {
                return false;
            }

            if (!frame.InWorld || frame.Puppets == 0)
            {
                return false;
            }

            if (!frame.HasPlayer || !frame.Alive || frame.Dying)
            {
                return false;
            }

            if (frame.ForbidInputs)
            {
                return false;
            }

            return frame.HasBody && !frame.Kinematic;
        }

        /// The largest x or z extent of our player's enabled solid
        /// colliders; DefaultRadius at MinRadius or less, or above
        /// MaxRadius.
        public static float Radius(IReadOnlyList<ColliderFacts> colliders)
        {
            float radius = 0f;
            for (int i = 0; i < colliders.Count; i++)
            {
                var c = colliders[i];
                if (c.Exists && !c.Trigger && c.Enabled)
                {
                    float wide = GameMath.Max(c.Extents.X, c.Extents.Z);
                    radius = GameMath.Max(radius, wide);
                }
            }

            if (radius <= MinRadius || radius > MaxRadius)
            {
                return DefaultRadius;
            }

            return radius;
        }

        /// How close two centres may be before they're eased apart: two
        /// body widths. Before a measurement (0), DefaultRadius's.
        public static float Distance(float radius)
        {
            if (radius > 0f)
            {
                return 2f * radius;
            }

            return 2f * DefaultRadius;
        }

        /// This frame's step for our player on the ground plane, or zero.
        /// Each shown, living, playing puppet nearer than Distance pushes
        /// it away by how deep they overlap (1 at the centre, 0 at the
        /// edge); right on top of each other, the lower id goes +x and the
        /// higher -x. The sum sets the way, and its length (at most 1)
        /// the share of MaxSpeed, for at most MaxDelta seconds.
        public static Vec2 Step(in PushFacts push)
        {
            float min = Distance(push.Radius);
            float x = 0f;
            float z = 0f;
            for (int i = 0; i < push.Puppets.Count; i++)
            {
                var other = push.Puppets[i];
                if (!other.Shown || !other.Alive || other.Spectating)
                {
                    continue;
                }

                float dx = push.Ours.X - other.At.X;
                float dz = push.Ours.Z - other.At.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist >= min)
                {
                    continue;
                }

                float deep = (min - dist) / min;
                if (dist > Overlap)
                {
                    x += dx / dist * deep;
                    z += dz / dist * deep;
                }
                else if (push.LocalId < other.Owner)
                {
                    x += deep;
                }
                else
                {
                    x -= deep;
                }
            }

            return Scale(x, z, push.Delta);
        }

        /// The summed push as a step: normalized, times MaxSpeed, its
        /// length held to 1, and the frame's time held to MaxDelta.
        static Vec2 Scale(float x, float z, Seconds delta)
        {
            if (x * x + z * z < ZeroSquared)
            {
                return default;
            }

            float length = (float)Math.Sqrt(x * x + z * z);
            float share = GameMath.Min(1f, length);
            float time = GameMath.Min(delta.Value, MaxDelta);
            float speed = MaxSpeed * share * time;
            if (!(length > NormalEpsilon))
            {
                return default;
            }

            return new Vec2(x / length * speed, z / length * speed);
        }
    }
}
