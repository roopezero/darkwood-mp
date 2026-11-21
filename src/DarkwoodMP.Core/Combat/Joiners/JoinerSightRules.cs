using System.Collections.Generic;
using DarkwoodMP.Combat.Targeting;

namespace DarkwoodMP.Combat.Joiners
{
    /// What a joiner "sees", for the enemies that react to being seen by
    /// the player: the forest spirit and the banshee (the plugin's
    /// JoinerSight and its patches, on the host).
    public static class JoinerSightRules
    {
        /// How far a stand-in sees at most.
        public const float SightRange = 800f;

        /// The field of view, in degrees, with no host player.
        public const float DefaultFov = 90f;

        /// The forest spirit teleports only when every player is further
        /// than this.
        public const float SpiritFar = 200f;

        /// How far past its target, along the target's up, the spirit
        /// lands.
        public const float SpiritRise = 500f;

        /// The host's rolls for the spirit's spot: from this, inclusive,
        /// to SpreadMax, exclusive (UnityEngine.Random.Range on ints).
        public const int SpreadMin = -100;

        public const int SpreadMax = 100;

        /// The effect the spirit teleports with.
        public const string SpiritEffect = "ForestSpirit_fastSpawnEff";

        /// How long the spirit's teleport effect takes.
        public const float SpiritEffectTime = 4f;

        /// How often a banshee a joiner sees checks its sight again.
        public const float BansheeCheckEvery = 0.5f;

        /// Whether a stand-in may see a thing, before the line of sight
        /// (Core.canSee), which the shell asks only when this is true.
        /// Out of SightRange it doesn't; else it does when within the
        /// half field of view or the light radius.
        public static bool InView(in ViewFacts view)
        {
            if (!view.StandInAlive || !view.Target)
            {
                return false;
            }

            if (view.Distance >= SightRange)
            {
                return false;
            }

            float fov = DefaultFov;
            if (view.HasHost)
            {
                fov = view.HostFov;
            }

            float light = 0f;
            if (view.HasLight)
            {
                light = view.LightRadius;
            }

            if (view.Angle >= fov / 2f && view.Distance >= light)
            {
                return false;
            }

            return true;
        }

        /// The nearest player's distance (JoinerSight.NearestPlayer): the
        /// host's player's, alive or not (float.MaxValue with none), and
        /// each living stand-in's.
        public static float Nearest(
            bool hasHost,
            float hostDistance,
            List<StandInSpot> standIns)
        {
            float best = float.MaxValue;
            if (hasHost)
            {
                best = hostDistance;
            }

            for (int i = 0; i < standIns.Count; i++)
            {
                var p = standIns[i];
                if (p.Alive)
                {
                    best = Min(best, p.Distance);
                }
            }

            return best;
        }

        /// The forest spirit's waitToTeleport while threat is on and this
        /// game hosts: it always replaces the game's, and teleports when it
        /// has a target nobody sees and every player is further than
        /// SpiritFar.
        public static TeleportStep Teleport(in TeleportFacts spirit)
        {
            if (!spirit.On || spirit.ClientMode)
            {
                return TeleportStep.Game;
            }

            if (spirit.HasTarget
                && !spirit.Seen
                && spirit.Nearest > SpiritFar)
            {
                return TeleportStep.Teleport;
            }

            return TeleportStep.Skip;
        }

        /// Where the spirit teleports to: SpiritRise along the target's up
        /// from it, plus the host's two rolls on the ground.
        public static Vec3 SpotOf(in TeleportFacts spirit)
        {
            var at = spirit.TargetAt;
            var up = spirit.TargetUp;
            return new Vec3(
                at.X + up.X * SpiritRise + spirit.RollX,
                at.Y + up.Y * SpiritRise + 0f,
                at.Z + up.Z * SpiritRise + spirit.RollZ);
        }

        /// Whether a banshee goes for a joiner who sees it instead of the
        /// game's sight check: threat is on, this game hosts, it is a
        /// living banshee, the host's player doesn't see it and a joiner
        /// does.
        public static bool BansheeGoesForJoiner(in BansheeFacts banshee)
        {
            if (!banshee.On
                || banshee.ClientMode
                || !banshee.Banshee
                || !banshee.Alive)
            {
                return false;
            }

            if (banshee.HostSees)
            {
                return false;
            }

            return banshee.JoinerSees;
        }

        /// Whether a banshee out of its player's sight keeps going to its
        /// target: threat is on, this game hosts, and the target is a
        /// living stand-in. It doesn't check that it is a banshee.
        public static bool BansheeKeepsChasing(in BansheeFacts banshee) =>
            banshee.On
            && !banshee.ClientMode
            && banshee.HasTarget
            && banshee.TargetStandIn
            && banshee.TargetAlive;

        /// canSeeEnemyFar for a banshee a joiner sees: the stand-in is
        /// nearer than its farViewDistance (Core.trueDistance).
        public static bool SeesFar(float distance, float far) =>
            distance < far;

        /// Whether the banshee goes to the stand-in (goToPos): not when
        /// its behaviour is defensive.
        public static bool Approaches(bool defensive) => !defensive;

        /// As Mathf.Min: a unless b is smaller.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }
    }
}
