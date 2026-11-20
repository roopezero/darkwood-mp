using System;
using System.Collections.Generic;

namespace DarkwoodMP.Sync.Players
{
    /// Where a player is placed so that they don't stand in another
    /// player or in a wall (SpawnSpot). The physics stays in the shell;
    /// this holds the candidates, the order and the tests that need none.
    /// Its body radius isn't SeparationRules'.
    public static class SpawnRules
    {
        /// Angles tried on each ring.
        public const int Angles = 12;

        /// The rings' count; RingAt gives each one's size.
        public const int RingCount = 5;

        /// The body radius without a capsule collider.
        public const float NoCapsuleRadius = 15f;

        /// The least body radius.
        public const float MinRadius = 10f;

        /// Kept between two players beside two body radii.
        public const float Gap = 10f;

        /// The sphere around the wanted spot whose colliders are
        /// background, in body radii.
        public const float BackgroundShare = 0.5f;

        /// The sphere around a candidate that must hold nothing solid, in
        /// body radii.
        public const float SphereShare = 1.1f;

        /// The most colliders one sphere query sees (SpawnSpot.Hits).
        public const int MaxHits = 64;

        /// The ground's layer, which is never in the way.
        public const int GroundLayer = 1;

        /// A candidate further than this from the wanted spot is checked
        /// for a wall between them.
        public const float RayFrom = 1f;

        /// Mathf.Deg2Rad.
        public const float Deg2Rad = 0.0174532924f;

        /// The size of ring i, in body radii from the wanted spot.
        public static float RingAt(int i)
        {
            switch (i)
            {
                case 0:
                    return 2.5f;
                case 1:
                    return 4f;
                case 2:
                    return 6f;
                case 3:
                    return 8f;
                default:
                    return 11f;
            }
        }

        /// The capsule's radius times the larger of the x and z scales, at
        /// least MinRadius; NoCapsuleRadius without a capsule.
        public static float BodyRadius(bool capsule, float radius, Vec2 scale)
        {
            float r = NoCapsuleRadius;
            if (capsule)
            {
                float wide = GameMath.Max(Math.Abs(scale.X), Math.Abs(scale.Z));
                r = radius * wide;
            }

            return GameMath.Max(MinRadius, r);
        }

        /// How far apart two players' centres are kept.
        public static float Apart(float radius) => radius * 2f + Gap;

        /// How far the search went before it gave up, for the log.
        public static float Searched(float radius) =>
            RingAt(RingCount - 1) * radius;

        /// A puppet's last position is kept clear when it has one, isn't a
        /// spectator, and is in our player's place
        /// (Locations.SamePlace(Away, Place)).
        public static bool KeepsClear(bool hasLast, bool spectating, bool same)
        {
            if (!hasLast || spectating)
            {
                return false;
            }

            return same;
        }

        /// The spots tried after the wanted one, in order: each ring from
        /// the smallest, Angles to a ring from the start angle (degrees,
        /// rolled in the placing player's game so that two joiners at once
        /// don't pick the same one). The wanted spot is tried before start
        /// is rolled, and is the answer when none of these is free.
        public static List<Vec3> Around(Vec3 want, float radius, float start)
        {
            var spots = new List<Vec3>(RingCount * Angles);
            for (int ring = 0; ring < RingCount; ring++)
            {
                float far = RingAt(ring) * radius;
                for (int a = 0; a < Angles; a++)
                {
                    float angle = (start + a * 360f / Angles) * Deg2Rad;
                    float cos = (float)Math.Cos(angle);
                    float sin = (float)Math.Sin(angle);
                    spots.Add(new Vec3(
                        want.X + cos * far,
                        want.Y + 0f * far,
                        want.Z + sin * far));
                }
            }

            return spots;
        }

        /// No other player is closer than apart to the spot on the ground
        /// plane. The first of SpawnSpot.Free's checks.
        public static bool ClearOfOthers(
            Vec2 at,
            IReadOnlyList<Vec2> others,
            float apart)
        {
            for (int i = 0; i < others.Count; i++)
            {
                if (Vec2.Distance(others[i], at) < apart)
                {
                    return false;
                }
            }

            return true;
        }

        /// A collider is in the way unless it's gone, a trigger, background,
        /// the ground, the player's own, or a stand-in.
        public static bool IsSolid(in SolidFacts collider)
        {
            if (!collider.Exists || collider.Trigger || collider.Background)
            {
                return false;
            }

            if (collider.Layer == GroundLayer)
            {
                return false;
            }

            if (collider.OfPlayer)
            {
                return false;
            }

            return !collider.OfProxy;
        }

        /// The pathfinding grid is asked only when the wanted spot is on
        /// it; then the candidate must be too.
        public static bool WalkableEnough(bool gridHere, bool walkable) =>
            !gridHere || walkable;

        /// The ray from the wanted spot is cast for a candidate further
        /// than RayFrom from it.
        public static bool ChecksRay(float length) => length > RayFrom;

        /// A hit on the ray is a wall between when it has no rigidbody and
        /// is solid.
        public static bool BlocksRay(bool rigidbody, bool solid) =>
            !rigidbody && solid;
    }
}
