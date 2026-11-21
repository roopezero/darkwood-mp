using System;

namespace DarkwoodMP.Combat.Motion
{
    /// The sums of Unity's Mathf and Vector3 that the motion uses, done
    /// the way Unity does them, so the core draws enemies where the
    /// shell's EnemyMotion did.
    static class MotionMath
    {
        /// As Mathf.Min: b unless a is smaller.
        public static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Max: b unless a is larger.
        public static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Abs.
        public static float Abs(float x) => Math.Abs(x);

        /// As Mathf.MoveTowards: target when it is within the step, else
        /// one step towards it.
        public static float MoveTowards(float from, float to, float step)
        {
            if (Abs(to - from) <= step)
            {
                return to;
            }

            if (to - from >= 0f)
            {
                return from + step;
            }

            return from - step;
        }

        /// The length of a move on the ground plane (x and z).
        public static float Flat(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        /// As (a - b).sqrMagnitude: in 3D.
        public static float SqrDistance(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }

        /// As Vector3.Lerp: t is held to 0 to 1.
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            if (t < 0f)
            {
                t = 0f;
            }

            if (t > 1f)
            {
                t = 1f;
            }

            return new Vec3(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        /// As a + (a - b) * k: on past a by k times the move from b.
        public static Vec3 Beyond(Vec3 a, Vec3 b, float k)
        {
            return new Vec3(
                a.X + (a.X - b.X) * k,
                a.Y + (a.Y - b.Y) * k,
                a.Z + (a.Z - b.Z) * k);
        }
    }
}
