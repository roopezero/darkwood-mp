using System.Collections.Generic;

namespace DarkwoodMP.World.Traders
{
    /// What TraderSync.Find looks at: the trader another game names, and
    /// the traders here.
    public readonly struct FindFacts
    {
        /// His name, as the other game sent it.
        public string Name { get; init; }

        /// His ref is valid (ObjRef.Valid).
        public bool Valid { get; init; }

        /// His ref is from the shared save (ObjRef.Kind.Saved).
        public bool Saved { get; init; }

        /// The ref names an NPC here (ObjRef.Resolve).
        public bool Resolved { get; init; }

        /// That NPC, when Resolved.
        public TraderCandidate ByRef { get; init; }

        /// Where the other game has him (x, z).
        public Vec2 At { get; init; }

        /// Every NPC here (FindObjectsOfType), in the order found.
        public IReadOnlyList<TraderCandidate> Candidates { get; init; }
    }
}
