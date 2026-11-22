using DarkwoodMP.Combat;
using DarkwoodMP.World.Traps;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// TrapSync's other calls: what touches a trap, and a trap switched.
    internal sealed partial class TrapSync
    {
        // ---- what touches a trap (Trigger.checkCollision) -------------------

        /// <summary>
        /// Before a trap checks what touched it. Client: notes the player's
        /// health before a trap bites, so a refused trap can give it back.
        /// False when the trap ignores it here: the host's enemies (and our
        /// frozen copies) set traps off on the host; ones of an area of our
        /// own do here.
        /// </summary>
        public bool BeforeCollision(Collider other)
        {
            if (NetEnemies.ClientMode && other != null
                && Player.Instance != null
                && other.GetComponent<Player>() != null)
            {
                _healthBefore = Player.Instance.health;
            }

            return !TrapRules.Ignores(TouchOf(other));
        }

        public void AfterCollision()
        {
            _healthBefore = -1f;
        }

        /// <summary>
        /// What the rule knows of what touched a trap, in its order and no
        /// further than it needs.
        /// </summary>
        private static TouchFacts TouchOf(Collider other)
        {
            var facts = new TouchFacts
            {
                ClientMode = NetEnemies.ClientMode,
                HasCollider = other != null,
            };

            if (!facts.ClientMode || !facts.HasCollider)
            {
                return facts;
            }

            var c = other.GetComponent<Character>();
            facts = facts with { Character = c != null };
            if (c == null)
            {
                return facts;
            }

            bool frozen = Coop.Current != null
                && Coop.Current.Enemies.Frozen.Contains(c);
            facts = facts with { Frozen = frozen };
            if (facts.Frozen)
            {
                return facts;
            }

            return facts with
            {
                Puppet = Coop.Current != null
                    && Coop.Current.Enemies.IsPuppet(c, out _),
            };
        }

        // ---- a trap switched (Item.switchTriggerState) ----------------------

        public void BeforeSwitch()
        {
            _coop.Guards.Depth++;
        }

        /// <summary>
        /// A trap disarmed (or failed to be): it's taken away, or left
        /// sprung, for everyone.
        /// </summary>
        public void AfterSwitch(Item item)
        {
            var guards = _coop.Guards;
            if (guards.Outermost() && guards.Sharing && item != null
                && item.GetComponent<Trigger>() != null)
            {
                WorldActions.Send(Action.TrapSwitched, item.gameObject);
            }
        }
    }
}
