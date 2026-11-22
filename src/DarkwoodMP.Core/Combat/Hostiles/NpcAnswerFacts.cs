namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: the host's answer to our NpcHit, and the NPC now.
    public readonly struct NpcAnswerFacts
    {
        /// The host accepted it.
        public bool Accepted { get; init; }

        /// The refusal's words, as the wire carries them.
        public string Reason { get; init; }

        /// The NPC is still there and alive.
        public bool Alive { get; init; }

        /// Our player is there (Player.Instance).
        public bool HasPlayer { get; init; }
    }
}
