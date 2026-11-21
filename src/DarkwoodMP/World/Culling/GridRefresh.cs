using UnityEngine;

namespace DarkwoodMP.World.Culling
{
    /// <summary>
    /// The arguments of a WorldGrid.refreshPosition call: the host's
    /// player's position, and the game's instant and force.
    /// </summary>
    internal struct GridRefresh
    {
        public Vector3 Pos;
        public bool Instant;
        public bool Force;
    }
}
