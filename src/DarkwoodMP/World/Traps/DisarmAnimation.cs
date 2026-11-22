using DarkwoodMP.World.Traps;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Disarming a trap shows. Some traps make the player crouch to disarm them
    /// (Trigger.bendDownToDisarm: the same animation as searching a container),
    /// others only show a progress bar, which is the player's own UI and isn't
    /// seen by the others. In co-op every trap is disarmed crouching, so the
    /// others see what you're doing (they see your character's sprites). The
    /// progress bar still shows, for you only.
    ///
    /// The crouch is the game's own: gettingInventory plays InventoryGet, then
    /// InventoryLoop while it lasts, and hidingInventory plays InventoryHide,
    /// at whose end the game lets the player go again.
    /// </summary>
    internal sealed class DisarmAnimation
    {
        /// <summary>
        /// We made the current disarm crouch (the trap wouldn't have).
        /// </summary>
        private bool _crouching;

        /// <summary>
        /// After Player.doDisarm: crouch for a trap the game doesn't crouch
        /// for already.
        /// </summary>
        public void Crouch(Player p, Item item)
        {
            _crouching = false;
            if (!DisarmRules.Crouches(CrouchOf(p, item)))
            {
                return;
            }

            p.performingAction = true;
            p.gettingInventory = true;
            p.dodging = false;
            _crouching = true;
        }

        /// <summary>
        /// The disarm ended (done or interrupted): stand up from our crouch.
        /// </summary>
        public void StandUp(Player p)
        {
            if (!_crouching)
            {
                return;
            }

            _crouching = false;
            p.hidingInventory = true;
            p.gotInventory = false;
        }

        /// <summary>
        /// What the crouch rule knows of a disarm starting: the only code
        /// that reads it for the rule, in its order and no further than it
        /// needs.
        /// </summary>
        private static CrouchFacts CrouchOf(Player p, Item item)
        {
            CoopSession coop = Coop.Current;
            var facts = new CrouchFacts
            {
                InCoop = coop != null && coop.Pause.InCoop,
                HasItem = item != null,
            };

            if (!facts.InCoop || !facts.HasItem)
            {
                return facts;
            }

            facts = facts with { Disarming = p.disarmingTrap };
            if (!facts.Disarming)
            {
                return facts;
            }

            var trigger = item.GetComponent<Trigger>();
            if (trigger == null)
            {
                return facts;
            }

            return facts with
            {
                HasTrigger = true,
                BendsDown = trigger.bendDownToDisarm,
            };
        }
    }
}
