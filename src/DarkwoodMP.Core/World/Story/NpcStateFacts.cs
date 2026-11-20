namespace DarkwoodMP.World.Story
{
    /// What the host knows when a joiner sends an NPC's state ("n:"):
    /// the host's NPC, and whether the joiner's kill of it was checked
    /// (NpcStateRules.Judge).
    public readonly struct NpcStateFacts
    {
        /// The joiner's value parses as JSON with a bool or nothing at
        /// "dead" (JObject.Parse(value).Value<bool?>("dead") didn't throw).
        public bool Readable { get; init; }

        /// The joiner's state says the NPC is dead (its "dead" is true;
        /// missing or null is false).
        public bool TheirsDead { get; init; }

        /// The host has a state for this NPC (Flags.getNPCState isn't
        /// null).
        public bool OursKnown { get; init; }

        /// The host's state says the NPC is dead (NPCState.dead).
        public bool OursDead { get; init; }

        /// A NpcDied request of a joiner's for this NPC was allowed within
        /// the window (AllowedKills.Recent).
        public bool KillAllowed { get; init; }
    }
}
