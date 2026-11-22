namespace DarkwoodMP.Combat.Enemies
{
    /// Which characters the host runs for everyone and sends to its
    /// clients, and when it stops (the plugin's NetEnemies: Eligible,
    /// InPlay, KindOf and the scan in HostUpdate).
    public static class EnemyTrackRules
    {
        /// How often the host looks for characters to track and untrack.
        public const float ScanInterval = 0.5f;

        /// How long an NPC stays calm before it's each player's own again
        /// (the plugin's HostileNpcs.CalmTime, which the scan reads).
        public const float CalmTime = 8f;

        /// A character the host runs and its clients show: every
        /// creature, and an NPC while HostileNpcs runs it. The shell
        /// answers false for a null character.
        public static bool Eligible(bool npc, bool running)
        {
            return !npc || running;
        }

        /// Still in the fight: alive, or in a pre-death stage with health
        /// left (Character.hasPreDeath). The shell answers false for a
        /// null character.
        public static bool InPlay(bool alive, bool preDeath, float health)
        {
            return alive || (preDeath && health > 0f);
        }

        /// The scan, once it's due: a character it hasn't got that is
        /// eligible gets the next id.
        public static bool Tracks(bool eligible, bool tracked)
        {
            return eligible && !tracked;
        }

        /// The scan, for one character it tracks. A creature, or an NPC
        /// out of play, is kept while it exists; an NPC that stays calm
        /// for more than CalmTime is let go.
        public static CalmStep Calm(in CalmFacts c)
        {
            if (!c.Exists)
            {
                return CalmStep.Gone;
            }

            if (!c.Npc || !c.InPlay)
            {
                return CalmStep.Keep;
            }

            if (c.Hostile)
            {
                return CalmStep.Hostile;
            }

            if (!c.Calming)
            {
                return CalmStep.StartCalm;
            }

            if ((c.Now - c.CalmSince).Value > CalmTime)
            {
                return CalmStep.Gone;
            }

            return CalmStep.Keep;
        }

        /// What kind of character it is, as the client compares it: its
        /// SaveableObject.prefabName (null when it has no SaveableObject),
        /// else its name without "(Clone)".
        public static string KindOf(string prefab, string name)
        {
            if (!string.IsNullOrEmpty(prefab))
            {
                return prefab;
            }

            return name.Replace("(Clone)", "").Trim();
        }

        /// Whether a check on a timer is due: now has reached the time it
        /// was set to next (now >= next, as every timer here compares).
        public static bool Due(Seconds now, Seconds next)
        {
            return now.Value >= next.Value;
        }
    }
}
