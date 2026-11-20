namespace DarkwoodMP.Combat.Enemies
{
    /// Client: what the host's states do to a copy of its enemy, and how
    /// a dead copy becomes a corpse (the plugin's NetEnemies.OnState,
    /// SetHealth, MovePuppets, Settle, ApplyAnim, CopyFrameEvent,
    /// StopSounds, PlayIdleLoop and LocationCharacters). A copy's own
    /// animation events never act: only TweenShadows is played.
    public static class CopyStateRules
    {
        /// How long the host's death animation is shown before the corpse
        /// is settled here (unscaled).
        public const float DeathAnimTime = 2f;

        /// The least health change a state makes.
        public const float HealthStep = 0.01f;

        /// How far a copy's frame may be from the host's in the same clip
        /// before it's played again from the host's.
        public const int MaxFramesOff = 2;

        /// The one animation event a copy plays.
        public const string TweenShadows = "TweenShadows";

        /// A living copy's health from the host's fraction of its maximum.
        /// No change with no maximum, or under HealthStep.
        public static HealthChange Health(
            float max,
            float health,
            float fraction)
        {
            if (max <= 0f)
            {
                return default;
            }

            float hp = Clamp01(fraction) * max;
            if (Abs(hp - health) < HealthStep)
            {
                return default;
            }

            return new HealthChange
            {
                Changes = true,
                Health = hp,
                Lost = hp < health,
            };
        }

        /// A living copy is put to sleep or woken as the host's is.
        public static bool SleepChanges(bool alive, bool sleeping, bool asleep)
        {
            return alive && sleeping != asleep;
        }

        /// After the sleep changes, its idle loop is played again: going
        /// to sleep, or with no loop playing, or still the sleeping one
        /// (the host's loop on waking may have come first).
        public static bool ReplaysIdle(in SleepFacts sleep)
        {
            return sleep.Asleep || !sleep.Playing || sleep.SleepingLoop;
        }

        /// The host's state says it's dead and cut in half.
        public static bool MarksCut(bool alive, bool cut)
        {
            return !alive && cut;
        }

        /// The host's state says it's dead, and it hasn't died here yet.
        public static bool FirstDeath(bool alive, bool timed)
        {
            return !alive && !timed;
        }

        /// Its health as it dies here: no more than the host's fraction
        /// of its maximum. Unchanged with no maximum.
        public static float DeathHealth(
            float max,
            float health,
            float fraction)
        {
            if (max <= 0f)
            {
                return health;
            }

            float hp = Clamp01(fraction) * max;
            if (health < hp)
            {
                return health;
            }

            return hp;
        }

        /// A dead copy whose death has played for DeathAnimTime is made a
        /// corpse, once.
        public static bool Settles(in SettleFacts copy)
        {
            return copy.Exists
                && copy.Dead
                && !copy.Settled
                && copy.SinceDeath.Value > DeathAnimTime;
        }

        /// A copy is drawn only once a state came for it.
        public static bool Draws(bool stated, bool exists)
        {
            return stated && exists;
        }

        /// A copy that isn't a corpse yet and was switched off here is
        /// switched on again.
        public static bool SwitchesOn(bool activeSelf, bool settled)
        {
            return !activeSelf && !settled;
        }

        /// How a copy is settled: cut in half first, then without a
        /// sprite animator, else the game's corpse steps.
        public static SettleStep SettleAs(bool cut, bool animated)
        {
            if (cut)
            {
                return SettleStep.Cut;
            }

            if (!animated)
            {
                return SettleStep.Collider;
            }

            return SettleStep.Corpse;
        }

        /// What a copy's animator does with the host's clip and frame.
        public static ClipStep Clip(in ClipFacts a)
        {
            if (a.Clip == 0 || !a.Library)
            {
                return ClipStep.Skip;
            }

            if (!a.Same)
            {
                return ClipStep.Switch;
            }

            int off = a.Current - a.Frame;
            if (off < 0)
            {
                off = -off;
            }

            if (off > MaxFramesOff && a.HasFrames && a.Frame < a.Frames)
            {
                return ClipStep.Replay;
            }

            return ClipStep.Keep;
        }

        /// The frame a switched clip starts from: the host's, within the
        /// clip's frames.
        public static int StartFrame(int frame, int frames)
        {
            int last = frames - 1;
            if (last < 0)
            {
                last = 0;
            }

            if (frame < 0)
            {
                return 0;
            }

            if (frame > last)
            {
                return last;
            }

            return frame;
        }

        /// Paused or resumed as the host's animator is.
        public static PlayStep Play(bool playing, bool isPlaying, bool paused)
        {
            if (!playing && isPlaying)
            {
                return PlayStep.Pause;
            }

            if (playing && paused)
            {
                return PlayStep.Resume;
            }

            return PlayStep.None;
        }

        /// A copy's animation event is played: TweenShadows only.
        public static bool PlaysEvent(string info)
        {
            return info == TweenShadows;
        }

        /// TweenShadows hides the shadows when its eventInt is 0.
        public static bool HidesShadows(int value)
        {
            return value == 0;
        }

        /// Silencing a copy keeps an AudioObject playing: its death cry,
        /// when the death sound is kept.
        public static bool KeepsSound(
            bool keepDeath,
            string death,
            string audio)
        {
            return keepDeath && !string.IsNullOrEmpty(death) && audio == death;
        }

        /// The idle loop a shown copy starts with: none when it's dead,
        /// its sleeping loop while asleep.
        public static IdleLoop IdleLoopFor(bool alive, bool asleep)
        {
            if (!alive)
            {
                return IdleLoop.None;
            }

            if (asleep)
            {
                return IdleLoop.Sleeping;
            }

            return IdleLoop.Idle;
        }

        /// A dead copy made here is switched with the location it lies in.
        public static bool SwitchesWithLocation(in CorpseFacts c)
        {
            return c.MadeHere && c.Exists && !c.Alive && !c.Already && c.Within;
        }

        /// As Mathf.Clamp01 (NaN stays NaN).
        static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        static float Abs(float value)
        {
            if (value < 0f)
            {
                return -value;
            }

            return value;
        }
    }
}
