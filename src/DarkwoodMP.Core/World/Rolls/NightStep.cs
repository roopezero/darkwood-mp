namespace DarkwoodMP.World.Rolls
{
    /// The kinds of scripted event step (GameEvent.Type) that
    /// RollRules.Presents tells apart. The shell maps the game's type to
    /// one of these; every other type is Other.
    public enum NightStep : byte
    {
        Other = 0,
        Sound,
        ChangeIdleSound,

        /// GameEvent.Type.playSound_NIEUZYWAJ.
        PlaySoundUnused,
        Particle,
        CameraEffect,
        TweenColor,
        PlayAnim,
        DisplayMessage,
        Debug,
        FireEvent,
    }
}
