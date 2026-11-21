using System;

namespace DarkwoodMP.World.Throws
{
    /// Thrown and dropped things, shared (the plugin's ThrowSync): what a
    /// player's throw sends, what the host lets through and relays, and
    /// what becomes of the copy in the other games.
    public static class ThrowRules
    {
        /// Host: how far from their stand-in a player may throw from, on
        /// the ground (ThrowSync.OnThrown's Authority.Near).
        public const float ThrowReach = 400f;

        /// The distance sent for something dropped at the player's feet
        /// (Player.dropItem), not thrown.
        public const float DroppedDistance = -1f;

        /// How long after its flight a copy of a thing that can be picked
        /// up again goes (the thrower's lands; this one doesn't).
        public const float RecoverDelay = 2f;

        /// Whether a throw or drop of ours is shared (ThrowSync.Thrown):
        /// while sharing, for a held thing of a known type that is a
        /// ThrownItem with a Rigidbody.
        public static bool Shares(in ThrowFacts throwing)
        {
            if (!throwing.Sharing || !throwing.Held)
            {
                return false;
            }

            if (string.IsNullOrEmpty(throwing.Type))
            {
                return false;
            }

            return throwing.Thrown && throwing.Body;
        }

        /// After Player.dropItem: whether it dropped what the player held.
        /// They held something before, and hold nothing now.
        public static bool WasDropped(bool heldBefore, bool heldNow)
        {
            return heldBefore && !heldNow;
        }

        /// What a throw sends: its velocity, where it lands, and the
        /// distance from the thrower to there (in three dimensions, as
        /// Vector3.Distance).
        public static ThrowShot Thrown(Vec3 velocity, Vec3 land, Vec3 from)
        {
            return new ThrowShot
            {
                Velocity = velocity,
                Target = land,
                Distance = Distance(land, from),
            };
        }

        /// What a drop sends: no velocity, where it is as the target, and
        /// DroppedDistance.
        public static ThrowShot Dropped(Vec3 pos)
        {
            return new ThrowShot
            {
                Target = pos,
                Distance = DroppedDistance,
            };
        }

        /// Whether a received throw is a drop: its distance is below 0.
        public static bool IsDrop(float distance)
        {
            return distance < 0f;
        }

        /// What a game does with an ItemThrown (ThrowSync.OnThrown). Not in
        /// a world, or on a client from anyone but the host: nothing. The
        /// host refuses one thrown from further than ThrowReach from the
        /// sender's stand-in (logged, not answered); else it relays it to
        /// the others with the sender as the thrower, whoever the message
        /// named. A copy is made unless this game's player threw it.
        public static ThrowReceipt Receive(in ThrownFacts thrown)
        {
            if (!thrown.InWorld)
            {
                return default;
            }

            if (thrown.IsClient && thrown.From != thrown.HostId)
            {
                return default;
            }

            if (thrown.IsHost && !MayThrow(thrown))
            {
                return new ThrowReceipt { Refused = true };
            }

            var by = thrown.ReportedBy;
            if (thrown.IsHost)
            {
                by = thrown.From;
            }

            return new ThrowReceipt
            {
                Relays = thrown.IsHost,
                Copies = by != thrown.LocalId,
                By = by,
            };
        }

        /// Host: the thrower is within ThrowReach of where it left from.
        /// True when they have no stand-in (ReachRules.Near).
        static bool MayThrow(in ThrownFacts thrown)
        {
            var reach = new ReachFacts
            {
                Known = thrown.Known,
                StandIn = thrown.StandIn,
                Target = thrown.Pos,
                Reach = ThrowReach,
            };

            return ReachRules.Near(reach);
        }

        /// What becomes of a copy of another's throw when it lands. A drop
        /// lies where it fell. A thrown thing that can be picked up again
        /// goes after its flight (GoesAfter): the thrower's lands. On the
        /// host, every other copy is kept in its save.
        public static CopyEnd Landing(in ThrowCopyFacts copy)
        {
            if (!IsDrop(copy.Distance) && copy.Recoverable)
            {
                return CopyEnd.Goes;
            }

            if (copy.IsHost)
            {
                return CopyEnd.Saved;
            }

            return CopyEnd.Stays;
        }

        /// When a copy that goes (CopyEnd.Goes) is destroyed: its flight
        /// time (ThrownItem.flyTime) and RecoverDelay.
        public static Seconds GoesAfter(float flyTime)
        {
            return new Seconds(flyTime + RecoverDelay);
        }

        /// As Vector3.Distance.
        static float Distance(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
