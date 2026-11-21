namespace DarkwoodMP.World.Npcs
{
    /// Host: one NPC at a tick, for NpcPlaceRules.Sends.
    public readonly struct NpcSendFacts
    {
        /// The tick sends all of them.
        public bool All { get; init; }

        /// It was sent before.
        public bool Sent { get; init; }

        /// Where it was when last sent, when Sent.
        public Vec2 Was { get; init; }

        /// Where it is: its Character's position, x and z.
        public Vec2 At { get; init; }
    }
}
