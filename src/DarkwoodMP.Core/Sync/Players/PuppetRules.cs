namespace DarkwoodMP.Sync.Players
{
    /// How a puppet takes another player's snapshots: when they died,
    /// where it's drawn, whether it's seen and which lights it shows
    /// (RemotePlayer).
    public static class PuppetRules
    {
        /// The tests' dummy (--dwmp-dummy), a copy of our player left
        /// standing: never a real peer id.
        public const ulong DummyId = ulong.MaxValue - 1;

        /// Further than this on the ground between two snapshots (placed on
        /// joining, a teleport) is shown at once instead of slid across.
        public const float JumpDistance = 300f;

        /// Alive, as the puppet says: true before the first snapshot.
        public static bool IsAlive(in PuppetFacts puppet) =>
            !puppet.HasLast || puppet.Alive;

        /// A spectator: false before the first snapshot.
        public static bool IsSpectating(in PuppetFacts puppet) =>
            puppet.HasLast && puppet.Spectating;

        /// Away: false before the first snapshot.
        public static bool IsAway(in PuppetFacts puppet) =>
            puppet.HasLast && puppet.Away;

        /// They died: the snapshot before said alive and this one doesn't.
        public static bool Died(bool hadLast, bool wasAlive, bool alive) =>
            hadLast && wasAlive && !alive;

        /// The new snapshot is further than JumpDistance from the last on
        /// the ground plane.
        public static bool Jumps(bool hadLast, Vec2 last, Vec2 next)
        {
            if (!hadLast)
            {
                return false;
            }

            return Vec2.Distance(next, last) > JumpDistance;
        }

        /// The last snapshot becomes the previous one, to blend from,
        /// unless there was none or the new one jumps.
        public static bool KeepsPrevious(bool hadLast, Vec2 last, Vec2 next)
        {
            if (!hadLast)
            {
                return false;
            }

            return !Jumps(hadLast, last, next);
        }

        /// One snapshot interval behind: the blend from the previous
        /// snapshot to the last over the time the last took to arrive.
        /// Without a previous one, or when the last isn't later, the last
        /// as it is.
        public static PuppetBlend Blend(in BlendFacts times)
        {
            bool later = times.Last.Value > times.Previous.Value;
            if (!times.HasPrevious || !later)
            {
                return default;
            }

            float since = times.Now.Value - times.Last.Value;
            float took = times.Last.Value - times.Previous.Value;
            return new PuppetBlend
            {
                Blends = true,
                Fraction = GameMath.Clamp01(since / took),
            };
        }

        /// The puppet is seen: not a spectator, and in the same place as
        /// our player (Locations.SamePlace(Away, Place)).
        public static bool Shown(bool spectating, bool samePlace) =>
            !spectating && samePlace;

        /// The held light the puppet shows: theirs while alive and not a
        /// spectator, else "". Away doesn't hide it.
        public static string HeldLight(in PuppetFacts puppet)
        {
            if (!puppet.Alive || puppet.Spectating)
            {
                return "";
            }

            if (puppet.HeldLight == null)
            {
                return "";
            }

            return puppet.HeldLight;
        }

        /// The held light is made again when another is wanted, or when one
        /// is wanted and the shown one is gone.
        public static bool RedoesHeld(string want, string shown, bool has)
        {
            if (want != shown)
            {
                return true;
            }

            return want != "" && !has;
        }

        /// The beam is on while their flashlight is, they're alive and not
        /// a spectator.
        public static bool Beam(in PuppetFacts puppet) =>
            puppet.Flashlight && puppet.Alive && !puppet.Spectating;

        /// The beam's radius and grey are set only when their game sent a
        /// radius; otherwise the copy keeps what our own beam had.
        public static bool SetsBeam(in PuppetFacts puppet) =>
            puppet.BeamRadius > 0f;

        /// The lantern's radius: theirs while alive, not a spectator and
        /// not away, else 0.
        public static float Lantern(in PuppetFacts puppet)
        {
            if (!puppet.Alive || puppet.Spectating || puppet.Away)
            {
                return 0f;
            }

            return puppet.Lantern;
        }

        /// At 0 or less the lantern is taken off the puppet.
        public static bool RemovesLantern(float want) => want <= 0f;
    }
}
