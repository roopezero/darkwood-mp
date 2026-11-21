namespace DarkwoodMP.World.Rolls
{
    /// What the world rolls by itself is rolled once, on the host: the
    /// night's scenario and events, the day's random events, the shadows'
    /// lights, the dawn's random objects, night mushrooms and the weather
    /// (the plugin's WorldEvents). A joiner in the shared world follows.
    public static class RollRules
    {
        /// Seconds between two of the host's looks at its scenario and
        /// weather (unscaled time).
        public const float CheckEvery = 1f;

        /// Seconds after which the host sends its scenario and weather
        /// again, changed or not.
        public const float Resend = 10f;

        /// Seconds after the dawn's random objects are made that the host
        /// sends them, once their Start has filled what they hold.
        public const float ObjectsAfter = 2f;

        /// Whether this game follows the host's rolls: it plays the host's
        /// world and isn't away in an outside location or a dream, which
        /// play as in single player. The dawn's objects and the weather
        /// follow the client alone, away or not.
        public static bool Follows(bool client, bool outside, bool dreaming)
        {
            return client && !(outside || dreaming);
        }

        /// Whether a joiner takes one of the host's roll messages: from the
        /// host, while in a world.
        public static bool FromHost(ulong from, ulong host, bool inWorld)
        {
            return from == host && inWorld;
        }

        /// Whether a RandomEvent fires here: always unless this game
        /// follows; then only a location event while it plays the host's
        /// night event.
        public static bool RunsRandomEvent(
            bool follows,
            bool presenting,
            bool locationEvent)
        {
            return !follows || (presenting && locationEvent);
        }

        /// The steps a joiner runs when it plays the host's night event:
        /// what's seen and heard. A fireEvent runs other GameEvents
        /// (filtered the same way), unless it fires a world event
        /// (activeModifier).
        public static bool Presents(NightStep step, bool activeModifier)
        {
            switch (step)
            {
                case NightStep.Sound:
                case NightStep.ChangeIdleSound:
                case NightStep.PlaySoundUnused:
                case NightStep.Particle:
                case NightStep.CameraEffect:
                case NightStep.TweenColor:
                case NightStep.PlayAnim:
                case NightStep.DisplayMessage:
                case NightStep.Debug:
                    return true;
                case NightStep.FireEvent:
                    return !activeModifier;
                default:
                    return false;
            }
        }

        /// What happens to a GameEvent step's coroutine: untouched unless a
        /// joiner that follows plays the host's night event (presenting:
        /// WorldEvents.Presentation above 0); then presented or dropped.
        public static StepPlay PlayStep(
            bool presenting,
            bool follows,
            bool presents)
        {
            if (!presenting || !follows)
            {
                return StepPlay.Untouched;
            }

            if (presents)
            {
                return StepPlay.Presented;
            }

            return StepPlay.Dropped;
        }

        /// Who turns the shadows' lights off. hostSharing: there is a
        /// session, this game hosts it, the world is shared
        /// (Coop.Guards.Sharing) and WorldEvents exists.
        public static ShadowTurn ShadowLights(bool follows, bool hostSharing)
        {
            if (follows)
            {
                return ShadowTurn.Skip;
            }

            if (hostSharing)
            {
                return ShadowTurn.HostRolls;
            }

            return ShadowTurn.Game;
        }

        /// A new id for an object the host spawned, from two rolls of
        /// System.Random.Next: the first above bit 31, the second below.
        public static long ObjectId(int first, int second)
        {
            return ((long)first << 31) | (uint)second;
        }

        /// Whether a dawn object's chunk or object index is one this game
        /// has; the object is skipped when either isn't.
        public static bool InRange(int index, int count)
        {
            return index < count;
        }

        /// Whether a client makes the host's night mushroom: it plays the
        /// host's world, and the id isn't one it has made.
        public static bool TakesNightObject(bool client, bool known)
        {
            return client && !known;
        }

        /// Whether the host sends a night mushroom: the chunk's night list
        /// grew, and the player's big location has a night mushroom.
        public static bool SendsNightObject(
            int before,
            int after,
            bool mushroom)
        {
            return after > before && mushroom;
        }
    }
}
