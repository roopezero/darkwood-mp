using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What thrown things are doing in one loaded world, for ThrownHits: the
    /// copies of others' throws, what thrown things spawned, and whether a
    /// thrown thing's impact, explosion or a flame is running now. Kept on
    /// WorldScope, so a new world starts with none.
    /// </summary>
    internal sealed class ThrownThings
    {
        /// Copies of others' throws in this world.
        public readonly HashSet<GameObject> Copies = new HashSet<GameObject>();

        /// What thrown things spawned as they landed or exploded (fire,
        /// fuel...).
        public readonly HashSet<GameObject> Spawned =
            new HashSet<GameObject>();

        /// How deep in a thrown thing's impact or explosion we are.
        public int Depth;

        /// The flame touching something right now (Flame.onCollideWith).
        public Flame Flame;
    }
}
