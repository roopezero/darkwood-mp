namespace DarkwoodMP.Combat.Enemies
{
    /// The sound loops of the host's enemies, made sure of in the clients'
    /// games (the plugin's NetEnemies.LoopCheck, IsLoop and OnSound, and
    /// CharacterSoundsPlayIdleLoopStop).
    public static class EnemyLoopRules
    {
        /// How often the host checks its enemies' loops (and armour).
        public const float LoopCheckInterval = 0.5f;

        /// How often each client is told every loop again.
        public const float LoopResendInterval = 2f;

        /// One of the loops playIdleLoop plays for this character.
        public static bool IsLoop(in LoopNames loops, string audio)
        {
            if (!loops.Exists)
            {
                return false;
            }

            return audio == loops.Idle
                || audio == loops.IdleAggressive
                || audio == loops.Aggressive
                || audio == loops.Defensive
                || audio == loops.Sleeping
                || audio == loops.Escaping;
        }

        /// The loop an enemy plays now: its idle AudioObject's audioID
        /// while it plays one, else "". Playing is false with no idle
        /// AudioObject.
        public static string CurrentLoop(bool playing, string audio)
        {
            if (playing && !string.IsNullOrEmpty(audio))
            {
                return audio;
            }

            return "";
        }

        /// What one tracked enemy's clients are sent in a loop check. A
        /// change goes reliably; the same loop again only when the resend
        /// is due. A sound playing that isn't one of its loops sends
        /// nothing. The shell then remembers Current as told.
        public static LoopSend Check(in LoopFacts loop)
        {
            if (!loop.Exists || loop.Dead || !loop.InPlay)
            {
                return LoopSend.None;
            }

            if (loop.Current != "" && !loop.IsLoop)
            {
                return LoopSend.None;
            }

            if (loop.Current != loop.Told)
            {
                return LoopSend.Changed;
            }

            if (loop.Resend)
            {
                return LoopSend.Again;
            }

            return LoopSend.None;
        }

        /// Host, around CharacterSounds.playIdleLoop: a loop stopped with
        /// none after it (an idle AudioObject before, none after, no loop
        /// asked for), which the clients are sent as an empty EnemySound.
        public static bool Stopped(bool before, string asked, bool after)
        {
            return before && string.IsNullOrEmpty(asked) && !after;
        }
    }
}
