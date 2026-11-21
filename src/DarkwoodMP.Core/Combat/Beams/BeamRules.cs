using System;

namespace DarkwoodMP.Combat.Beams
{
    /// A joiner's flashlight on the host: the beam's shape, whose beam is
    /// on shadow armour and what it does to it, and the box the puppet's
    /// light lights (the plugin's ShadowArmorJoinerLightPatch and
    /// PuppetBeamLightPatch). Players are rotated Euler(90, yaw, 0), so a
    /// puppet's local up is where it faces.
    public static class BeamRules
    {
        /// How far ahead of the puppet the beam reaches.
        public const float MaxBeam = 1500f;

        /// How far behind the puppet the beam still counts.
        public const float Behind = 50f;

        /// The beam's half width at the puppet, before the widening.
        public const float BaseWidth = 50f;

        /// How much half width the beam gains per unit ahead.
        public const float Widening = 0.5f;

        /// The least half width of the beam.
        public const float MinWidth = 100f;

        /// A facing whose squared length on the ground is below this faces
        /// nowhere: no beam, and the game's box stays.
        public const float MinFacing = 0.01f;

        /// The puppet light box: centred this far ahead of the puppet.
        public const float BoxAhead = 500f;

        /// The box's side before turning, which covers the beam out to
        /// 1000 ahead.
        public const float BoxSide = 1000f;

        /// How much wider the box is, so it covers the square turned any
        /// way.
        public const float BoxTurned = 1.42f;

        /// The box's height.
        public const float BoxHeight = 10000f;

        /// The name a puppet's root starts with (RemotePlayer).
        public const string PuppetPrefix = "DarkwoodMP_Remote_";

        /// Whether a position is in the beam of a puppet at root facing up,
        /// on the ground: from Behind behind it to MaxBeam ahead, and
        /// within a half width growing from BaseWidth by Widening per unit
        /// ahead, at least MinWidth. Computed as Unity's Vector3 does.
        public static BeamSpot InBeam(Vec3 root, Vec3 up, Vec3 pos)
        {
            float fx = up.X;
            float fy = 0f;
            float fz = up.Z;
            float rx = pos.X - root.X;
            float ry = 0f;
            float rz = pos.Z - root.Z;
            if (fx * fx + fy * fy + fz * fz < MinFacing)
            {
                return default;
            }

            float mag = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
            fx /= mag;
            fy /= mag;
            fz /= mag;
            float ahead = rx * fx + ry * fy + rz * fz;
            float cx = fy * rz - fz * ry;
            float cy = fz * rx - fx * rz;
            float cz = fx * ry - fy * rx;
            float aside = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
            bool inside = ahead >= -Behind
                && ahead <= MaxBeam
                && aside <= Max(MinWidth, ahead * Widening + BaseWidth);
            return new BeamSpot
            {
                In = inside,
                Ahead = ahead,
                Aside = aside,
            };
        }

        /// Whether a stand-in's player may light things with a flashlight:
        /// the stand-in is alive, the puppet has a root, and the last
        /// state says flashlight on, alive and not spectating.
        public static bool LightsBeam(in BeamOwnerFacts owner) =>
            owner.StandInAlive
            && owner.Root
            && owner.HasSnapshot
            && owner.Flashlight
            && owner.Alive
            && !owner.Spectating;

        /// Whether the host looks for a joiner's beam on shadow armour:
        /// there is armour with health above 0 (NaN isn't), the host has
        /// stand-ins, and this game hosts.
        public static bool ChecksArmour(in ArmorFacts armour)
        {
            if (!armour.Exists
                || !(armour.Health > 0f)
                || armour.StandIns == 0)
            {
                return false;
            }

            return armour.Hosting;
        }

        /// Whether a beam hurts the armour: not when the game's own check
        /// already did, because the spot is lit (Core.isInLight) or the
        /// armour burns.
        public static bool HurtsArmour(bool lit, bool burning) =>
            !lit && !burning;

        /// What the beam does to the armour in one check: lightDamage
        /// times normalDamageMod times damageRefreshRate.
        public static float ArmourDamage(float light, float mod, float rate) =>
            light * mod * rate;

        /// Whether the armour's character turns on the joiner: there is
        /// one, alive and not a dummy. It attacks on sight unless
        /// defensive (JoinerAiRules.AttacksOnSight), and attacks the
        /// stand-in unless it is already its target.
        public static bool ArmourTurns(
            bool character,
            bool alive,
            bool dummy) =>
            character && alive && !dummy;

        /// Whether a light is a puppet's: its direct parent's name starts
        /// with PuppetPrefix (culture-sensitive, as the shell's StartsWith).
        public static bool IsPuppetLight(bool parent, string name) =>
            parent && name.StartsWith(PuppetPrefix);

        /// The box a puppet at parent facing up lights: BoxAhead ahead of
        /// it, BoxSide times BoxTurned square and BoxHeight high. Facing
        /// nowhere on the ground, the game's box stays.
        public static BeamBox PuppetBox(Vec3 parent, Vec3 up)
        {
            float fx = up.X;
            float fy = 0f;
            float fz = up.Z;
            if (fx * fx + fy * fy + fz * fz < MinFacing)
            {
                return default;
            }

            float mag = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
            float side = BoxSide * BoxTurned;
            var center = new Vec3(
                parent.X + fx / mag * BoxAhead,
                parent.Y + fy / mag * BoxAhead,
                parent.Z + fz / mag * BoxAhead);
            return new BeamBox
            {
                Changed = true,
                Center = center,
                Size = new Vec3(side, BoxHeight, side),
            };
        }

        /// As Mathf.Max: a unless b is larger.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
