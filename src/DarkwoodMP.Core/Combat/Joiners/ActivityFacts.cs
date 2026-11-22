namespace DarkwoodMP.Combat.Joiners
{
    /// A spawned character's activity looking for "the player" or "the
    /// player's location" (Character.Activity.assignTarget on the host).
    public readonly struct ActivityFacts
    {
        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// The activity and its character aren't null.
        public bool Exists { get; init; }

        /// NightSpawns.SentAt returned true.
        public bool Sent { get; init; }

        /// SentAt gave a location (a joiner's hideout).
        public bool SentLocation { get; init; }

        /// SentAt gave a stand-in.
        public bool SentStandIn { get; init; }

        /// That stand-in is alive.
        public bool SentAlive { get; init; }

        /// ThreatRules.Best picked a stand-in. Read only when the rule
        /// needs it.
        public bool BestStandIn { get; init; }

        /// Threat.BestPlayerFor isn't null (the host's player or a
        /// stand-in). Read only when the rule needs it.
        public bool BestPlayer { get; init; }

        /// NightSpawns.BigLocationAt the chosen stand-in's position isn't
        /// null. Read only when the rule needs it.
        public bool StandInLocation { get; init; }
    }
}
