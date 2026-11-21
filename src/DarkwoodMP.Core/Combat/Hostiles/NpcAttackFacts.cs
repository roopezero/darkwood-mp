namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: a scripted "attack the player" on an NPC of ours
    /// (HostileNpcs.ClientAttack, from ClientNpcAttackPatch).
    public readonly struct NpcAttackFacts
    {
        /// We are applying our own hit or attack (HostileNpcs._ours).
        public bool Ours { get; init; }

        /// The character is there and is an NPC.
        public bool IsNpc { get; init; }

        /// Character.dummy.
        public bool Dummy { get; init; }

        /// A client in the host's world (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// Only our game has it (NetEnemies.LocalOnly).
        public bool LocalOnly { get; init; }

        /// It shows one of the host's enemies (NetEnemies.IsPuppet).
        public bool Copy { get; init; }

        /// NetEnemies.Frozen holds it.
        public bool Frozen { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// ScriptedEvents.Current is Part.SharedOnly: another player's
        /// event, run here too.
        public bool SharedOnly { get; init; }
    }
}
