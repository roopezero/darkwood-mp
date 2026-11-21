namespace DarkwoodMP.World.Pause
{
    /// What the pause's every-frame check does. Zero: nothing.
    public readonly struct PauseTick
    {
        /// Set the time scale back to 1 and unpause the sound
        /// (AudioController.UnpauseAll(PauseRules.SoundFade)), with a log
        /// line: the game was paused before co-op started.
        public bool Unfreeze { get; init; }

        /// Zero the player's velocity, so a player in a menu doesn't slide.
        public bool StandStill { get; init; }
    }
}
