namespace DarkwoodMP.Combat.Enemies
{
    /// What the client does with an EnemySound (CopyRules.Sound).
    public enum SoundStep
    {
        /// Nothing.
        Ignore = 0,

        /// The host's loop stopped: playIdleLoop("").
        StopLoop,

        /// One of its loops: playIdleLoop(audio), in place of the copy's.
        Loop,

        /// Any other sound: played at the copy.
        Play,
    }
}
