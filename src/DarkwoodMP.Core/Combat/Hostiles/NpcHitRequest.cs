using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.World;

namespace DarkwoodMP.Combat.Hostiles
{
    /// Our player's hit on an NPC the host doesn't run yet, as NpcHit
    /// carries it.
    public readonly struct NpcHitRequest
    {
        public ObjName Npc { get; init; }
        public float Damage { get; init; }
        /// As EnemyHit's (NpcHitMessage.FlagsOf).
        public byte Flags { get; init; }
        /// The weapon's effects; none for null.
        public IReadOnlyList<SentEffect> Effects { get; init; }
        /// The weapon as its save state's JSON; "" for none.
        public string Weapon { get; init; }
    }
}
