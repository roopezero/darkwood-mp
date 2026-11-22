using System;

namespace DarkwoodMP.World.Doors
{
    /// A door leaf's angle and swing (the plugin's DoorLeaf). Angles are
    /// in degrees; a signed angle is the leaf's turn from shut, which the
    /// game leaves at a local z of 90 (Door.setCloseRotation).
    public static class LeafRules
    {
        /// The local z angle of a shut leaf.
        public const float ShutZ = 90f;

        /// A leaf turned further than this from shut is drawn open.
        public const float TurnedAngle = 10f;

        /// How far a leaf with no hinge limits turns: all the way round.
        public const float FullTurn = 180f;

        /// The save's range of a leaf's angle, either way.
        public const float SaveLimit = 105f;

        /// How near a body's world y must be to 0, 90, 180 or 270 for the
        /// push's side to follow that facing.
        public const float FacingSlack = 1f;

        /// The leaf's signed angle from shut, from its local z
        /// (Mathf.DeltaAngle(z, 90)), the value the mod saves as rot.
        /// A door with no leaf is at 0: the shell passes ShutZ.
        public static float Signed(float localZ) =>
            DeltaAngle(localZ, ShutZ);

        /// The leaf is drawn open: more than TurnedAngle from shut.
        public static bool Turned(float signed) =>
            Math.Abs(signed) > TurnedAngle;

        /// How far the leaf turns from shut either way: its hinge's
        /// larger limit, within 0 to 180, or 180 with no hinge or no
        /// limits.
        public static float MaxTurn(in HingeFacts hinge)
        {
            if (!hinge.Present || !hinge.UseLimits)
            {
                return FullTurn;
            }

            float max = Max(Math.Abs(hinge.Min), Math.Abs(hinge.Max));
            return Clamp(max, 0f, FullTurn);
        }

        /// The angle saved as Door.SaveState.rot for a leaf at signed.
        public static float SaveAngle(float signed) =>
            Clamp(signed, -SaveLimit, SaveLimit);

        /// How far to turn the leaf about world up, from its local z, to
        /// bring it to signed (held within maxTurn either way).
        public static float TurnBy(float localZ, float signed, float maxTurn)
        {
            float to = Clamp(signed, -maxTurn, maxTurn);
            return DeltaAngle(localZ, ShutZ - to);
        }

        /// The way Door.open pushes the leaf from where the opener stood:
        /// +1 along the body's up, or -1. Yaw is the body's world y
        /// (0 to 360). A yaw just under 360 isn't matched as 0 and takes
        /// the last branch.
        public static float Side(float yaw, Vec2 opener, Vec2 body)
        {
            bool zMore = opener.Z > body.Z;
            bool xMore = opener.X > body.X;
            if (Near(yaw, 180f))
            {
                return Sign(!zMore);
            }

            if (Near(yaw, 0f))
            {
                return Sign(zMore);
            }

            if (Near(yaw, 90f))
            {
                return Sign(xMore);
            }

            if (Near(yaw, 270f))
            {
                return Sign(!xMore);
            }

            return Sign(!xMore);
        }

        /// This game opened a door whose leaf is switched off here: keep
        /// the opener, and push the leaf open once it's on again
        /// (DoorLeaf.Opened).
        public static bool RemembersOpen(in LeafDoorFacts door) =>
            door.Exists && door.HasBody && door.Opened && !door.Active;

        /// A remembered door's leaf came on again: still open, not broken
        /// and not turned yet, so it swings open (DoorLeaf.Enabled).
        public static bool SwingsOnEnable(in LeafDoorFacts door, float signed)
        {
            return door.Opened
                && !door.Destroyed
                && door.HasBody
                && !Turned(signed);
        }

        /// One physics step after the leaf came on: push it, if the door
        /// is still open and its body free (DoorLeaf.Swing).
        public static bool PushesOpen(in LeafDoorFacts door)
        {
            return door.Exists
                && door.Opened
                && door.HasBody
                && door.HasRigidbody
                && !door.Kinematic;
        }

        /// From a player at p to the leaf on the ground: to the segment
        /// from the hinge through the leaf's centre and as far again, or
        /// to the centre with no hinge (DoorPush.Dist).
        public static float TouchDistance(in LeafShape leaf, Vec2 p)
        {
            if (!leaf.HasHinge)
            {
                return Vec2.Distance(p, leaf.Centre);
            }

            var h = leaf.Hinge;
            var c = leaf.Centre;
            float ex = h.X + 2f * (c.X - h.X);
            float ez = h.Z + 2f * (c.Z - h.Z);
            float hx = ex - h.X;
            float hz = ez - h.Z;
            float sq = hx * hx + hz * hz;
            float t = 0f;
            if (sq >= 1e-3f)
            {
                float dot = (p.X - h.X) * hx + (p.Z - h.Z) * hz;
                t = Clamp(dot / sq, 0f, 1f);
            }

            var on = new Vec2(h.X + t * hx, h.Z + t * hz);
            return Vec2.Distance(p, on);
        }

        /// As Mathf.DeltaAngle: the shortest turn from current to target,
        /// from -180 to 180.
        public static float DeltaAngle(float current, float target)
        {
            float d = Repeat(target - current, 360f);
            if (d > 180f)
            {
                d -= 360f;
            }

            return d;
        }

        /// As Mathf.Clamp.
        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        /// As Mathf.Repeat.
        static float Repeat(float t, float length)
        {
            float floor = (float)Math.Floor(t / length);
            return Clamp(t - floor * length, 0f, length);
        }

        /// As Mathf.Max.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        static bool Near(float yaw, float facing) =>
            yaw > facing - FacingSlack && yaw < facing + FacingSlack;

        static float Sign(bool plus)
        {
            if (plus)
            {
                return 1f;
            }

            return -1f;
        }
    }
}
