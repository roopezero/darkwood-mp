namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: an NPC whose weapon effects are activating, and the hit
    /// last left to the host (HostileNpcs.EffectsForwarded).
    public readonly struct ForwardFacts
    {
        /// We are applying our own hit or attack (HostileNpcs._ours).
        public bool Ours { get; init; }

        /// The NPC is there and is the one whose hit was left to the host.
        public bool Forwarded { get; init; }

        /// This frame (Time.frameCount).
        public int Frame { get; init; }

        /// The frame that hit was left to the host in.
        public int ForwardedFrame { get; init; }
    }
}
