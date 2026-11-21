using System;

namespace DarkwoodMP.World.Doors
{
    /// Another game's leaf angles here: whether to take one (the
    /// plugin's DoorPush.OnLeaf), and how this game's leaf turns towards
    /// it over the next frames (Glides).
    public static class LeafGlideRules
    {
        /// How far from a leaf a pusher may be.
        public const float Reach = ReachRules.Reach;

        /// How far past the leaf's MaxTurn a pushed angle may be.
        public const float PushSlack = 5f;

        /// A leaf this near the target, GlideRest after the last angle,
        /// has arrived.
        public const float CloseEnough = 0.5f;

        /// How fast a leaf closes on the target: the share of the way it
        /// turns each second, as 1 - e^(-Rate * dt) a frame.
        public const float Rate = 15f;

        /// A leaf near the target is let go this long after the last
        /// angle.
        public static readonly Seconds GlideRest = new Seconds(0.25f);

        /// A leaf is let go this long after the last angle, wherever it
        /// is.
        public static readonly Seconds GlideMax = new Seconds(1f);

        /// Another game's leaf angle arrived. The checks run in this
        /// order, and the first that fails wins.
        public static LeafAnswer Hear(in LeafHeardFacts heard)
        {
            if (!heard.InWorld)
            {
                return LeafAnswer.Dropped;
            }

            if (heard.Client && !heard.FromHost)
            {
                return LeafAnswer.Dropped;
            }

            if (float.IsNaN(heard.Angle) || float.IsInfinity(heard.Angle))
            {
                return LeafAnswer.Dropped;
            }

            var door = heard.Door;
            if (!door.Exists || !door.HasBody)
            {
                return LeafAnswer.NotFound;
            }

            if (!door.Opened || door.Destroyed || door.Barricaded)
            {
                return LeafAnswer.NotOpen;
            }

            if (!heard.Host)
            {
                return LeafAnswer.Take;
            }

            var pusher = heard.Pusher with { Reach = Reach };
            bool far = !ReachRules.Near(pusher);
            if (far || Math.Abs(heard.Angle) > heard.MaxTurn + PushSlack)
            {
                return LeafAnswer.PutBack;
            }

            return LeafAnswer.Relay;
        }

        /// One frame of a gliding leaf.
        public static GlideStep Glide(in GlideFacts glide)
        {
            var door = glide.Door;
            if (!door.Exists || !door.HasBody || !door.Opened || door.Destroyed)
            {
                return new GlideStep { Outcome = GlideOutcome.Stop };
            }

            float off = LeafRules.DeltaAngle(glide.Angle, glide.Target);
            float since = (glide.Now - glide.Heard).Value;
            bool near = Math.Abs(off) < CloseEnough && since > GlideRest.Value;
            if (near || since > GlideMax.Value)
            {
                return new GlideStep
                {
                    Outcome = GlideOutcome.Settle,
                    Angle = glide.Target,
                };
            }

            float k = Factor(glide.Delta);
            return new GlideStep
            {
                Outcome = GlideOutcome.Turn,
                Angle = glide.Angle + off * k,
            };
        }

        /// The share of the way to the target a leaf turns in a frame of
        /// this length: 1 - e^(-Rate * dt).
        public static float Factor(Seconds delta) =>
            1f - (float)Math.Exp(-Rate * delta.Value);
    }
}
