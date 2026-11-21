using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The arguments of a Character.getHit call that a client's patch looks
    /// at, so they travel together.
    /// </summary>
    internal struct GameHit
    {
        public float Damage;
        public Transform Attacker;
        public bool CanCutInHalf;
        public bool ByPlayer;
        public bool CanInterrupt;
        public bool NormalHit;
    }
}
