namespace DarkwoodMP.World.Pause
{
    /// The game doesn't stop while others play in it (the plugin's
    /// CoopPause): a pause only marks the game paused, and slow motion
    /// keeps the normal rate, since the host's game runs everyone's world.
    public static class PauseRules
    {
        /// The time scale that co-op keeps.
        public const float Normal = 1f;

        /// Seconds the sound takes to come back after a frozen pause.
        public const float SoundFade = 0.5f;

        /// Whether others share this world right now. Unlike
        /// TogetherRules.Together it doesn't look at WorldShare.Loading, so
        /// a connected client in a world is in co-op before its world is
        /// ready.
        public static bool InCoop(in CoopFacts coop)
        {
            if (!coop.Active || !coop.Connected || !coop.InWorld)
            {
                return false;
            }

            return coop.IsClient || coop.Peers > 0;
        }

        /// Whether Core.pause runs as the game's. In co-op it only sets
        /// Core.Paused instead.
        public static bool RunsGamePause(bool coop)
        {
            return !coop;
        }

        /// Whether a scripted event's slow motion
        /// (Controller.alterTimeScale) runs. In co-op it doesn't at all.
        public static bool RunsScriptedSlowMotion(bool coop)
        {
            return !coop;
        }

        /// The scale Core.setTimeScale gets. In co-op a scale above 0 that
        /// isn't 1 becomes 1; 0 (a pause) is let through.
        public static float TimeScaleFor(float scale, bool coop)
        {
            if (scale > 0f && scale != Normal && coop)
            {
                return Normal;
            }

            return scale;
        }

        /// What to do this frame: only in co-op with the game paused.
        public static PauseTick Tick(in PauseFacts pause)
        {
            if (!pause.InCoop || !pause.Paused)
            {
                return default;
            }

            bool still = pause.Alive && pause.HasBody && !pause.Kinematic;
            return new PauseTick
            {
                Unfreeze = pause.TimeScale == 0f,
                StandStill = still,
            };
        }
    }
}
