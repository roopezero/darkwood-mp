using DarkwoodMP.Net;
using DarkwoodMP.World.Breaking;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A hit, a break or a repair of an Item, and an Explodes going off,
    /// from another game (ItemHit, ItemBroken, ItemRepaired, Explode): the
    /// host's checks with BreakActionRules, the apply, and the item and
    /// explosive parts of ObjState. BreakSync sends them.
    /// </summary>
    internal sealed class BreakActions
    {
        private readonly CoopSession _coop;

        public BreakActions(CoopSession coop)
        {
            _coop = coop;
        }

        // ---- the host's checks ------------------------------------------

        /// <summary>
        /// Host: whether a client's game could have set off this explosive
        /// (WorldActions.Allowed). Refuses the request if not.
        /// </summary>
        public bool MayExplode(ObjRef obj)
        {
            var ex = ObjRef.Resolve<Explodes>(obj);
            var verdict = BreakActionRules.MayExplode(FactsOf(ex));
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// Host: whether a client's player could have hit, broken or
        /// repaired this item (an action from ItemHit up that no branch
        /// before names; x is a hit's damage). Refuses the request if not.
        /// </summary>
        public bool MayChange(Action a, ObjRef obj, float x)
        {
            var facts = FactsOf(ObjRef.Resolve<Item>(obj), x);
            Verdict verdict;
            switch (a)
            {
                case Action.ItemHit:
                    verdict = BreakActionRules.MayHit(facts);
                    break;
                case Action.ItemBroken:
                    verdict = BreakActionRules.MayBreak(facts);
                    break;
                case Action.ItemRepaired:
                    verdict = BreakActionRules.MayRepair(facts);
                    break;
                default:
                    verdict = BreakActionRules.MayDoUnknown(facts);
                    break;
            }

            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the break rules know of an explosive the host is asked
        /// about: the only code that reads it for them.
        /// </summary>
        private ExplodeRequestFacts FactsOf(Explodes ex)
        {
            if (ex == null)
            {
                return new ExplodeRequestFacts();
            }

            return new ExplodeRequestFacts
            {
                Exists = true,
                OnCharacter = ex.GetComponent<Character>() != null,
                WentOff = BreakSync.Activated(ex),
                Requester = _coop.Authority.ReachOf(
                    ex.transform.position, ex.name),
            };
        }

        /// <summary>
        /// What the break rules know of an item the host is asked about,
        /// with the hit's damage: the only code that reads it for them.
        /// </summary>
        private BreakFacts FactsOf(Item item, float damage)
        {
            if (item == null)
            {
                return new BreakFacts { Damage = damage };
            }

            return new BreakFacts
            {
                Exists = true,
                Destroyed = item.destroyed,
                Destructible = item.destructible,
                Damage = damage,
                Requester = _coop.Authority.ReachOf(
                    item.transform.position, item.name),
            };
        }

        // ---- the apply --------------------------------------------------

        /// <summary>
        /// An Explode from another game: set off our copy (flaming as the
        /// sender's). False when it isn't here or is an enemy's, and it
        /// goes no further.
        /// </summary>
        public bool Explode(ObjRef obj, bool flaming)
        {
            var ex = WorldActions.Got<Explodes>(Action.Explode, obj);
            if (ex == null || ex.GetComponent<Character>() != null)
            {
                return false; // never an enemy's (see BreakSync)
            }

            if (!BreakSync.Activated(ex))
            {
                ex.onActivate(flaming);
            }

            return true;
        }

        /// <summary>
        /// An ItemHit, ItemBroken or ItemRepaired (or an unknown action
        /// from ItemHit up) from another game, applied here. On the host a
        /// hit's Y becomes the health it left, for the others. False when
        /// the item isn't here, and it goes no further.
        /// </summary>
        public bool Apply(Action a, ObjRef obj, ref ActionValues values)
        {
            var item = WorldActions.Got<Item>(a, obj);
            if (item == null)
            {
                return false;
            }

            switch (a)
            {
                case Action.ItemHit:
                    Hit(item, ref values);
                    break;
                case Action.ItemBroken:
                    if (!item.destroyed)
                    {
                        item.die();
                    }

                    break;
                case Action.ItemRepaired:
                    if (item.destroyed)
                    {
                        item.setNormalState();
                    }

                    break;
            }

            return true;
        }

        private void Hit(Item item, ref ActionValues values)
        {
            if (item.destroyed)
            {
                return;
            }

            // A client's hit is applied to the host's health; the host's own
            // says what health it left, so a copy that missed a hit (or took
            // one more) comes back in step.
            if (!_coop.Net.IsHost && item.shadowArmor == null)
            {
                // a shadow takes the hit, not the item
                item.health = BreakActionRules.HealthBeforeHit(
                    values.Y, values.X);
            }

            item.getHit((int)values.X, null, values.F1);
            if (_coop.Net.IsHost)
            {
                values = values with { Y = item.health };
            }
        }

        // ---- the state to roll back to ----------------------------------

        /// <summary>
        /// Host: the item part of an ObjState (ItemObjState).
        /// </summary>
        public void WriteState(PacketWriter w, Item item)
        {
            ItemObjState.Write(w, HostOf(item));
        }

        /// <summary>
        /// The item as the host has it: the only code that reads it for an
        /// ObjState.
        /// </summary>
        private static HostItem HostOf(Item item)
        {
            var l = item.GetComponent<Locked>();
            var ii = item.GetComponent<InteractiveItem>();
            var feeder = item.GetComponent<Feeder>();
            return new HostItem
            {
                On = item.isOn,
                Locked = l != null && l.locked,
                Searched = item.searched,
                Broken = item.destroyed,
                Switched = ii != null && ii.isOn,
                FeederFull = feeder != null && feeder.Active,
                Health = item.health,
            };
        }

        /// <summary>
        /// Client: put our copy of this item as the host has it (go may be
        /// null, or not an item: the bytes are read either way).
        /// </summary>
        public void ReadState(PacketReader r, Transform go)
        {
            var host = ItemObjState.Read(r);
            Item item = null;
            if (go != null)
            {
                item = go.GetComponent<Item>();
            }

            if (item == null)
            {
                return;
            }

            RollBack(item, host);
        }

        private static void RollBack(Item item, in HostItem host)
        {
            if (item.isOn != host.On)
            {
                if (host.On)
                {
                    item.turnOn();
                }
                else
                {
                    item.turnOff();
                }
            }

            var l = item.GetComponent<Locked>();
            if (l != null)
            {
                l.locked = host.Locked;
            }

            item.searched = host.Searched;
            var ii = item.GetComponent<InteractiveItem>();
            if (ii != null)
            {
                ii.isOn = host.Switched;
            }

            var feeder = item.GetComponent<Feeder>();
            if (feeder != null && feeder.Active != host.FeederFull)
            {
                if (host.FeederFull)
                {
                    feeder.makeActive();
                }
                else
                {
                    feeder.makeInactive();
                }
            }

            if (host.Broken && !item.destroyed && item.destructible)
            {
                item.die();
            }
            else if (!host.Broken && item.destroyed)
            {
                item.setNormalState();
            }

            item.health = host.Health;
        }

        /// <summary>
        /// Host: the explosive part of an ObjState (ExplosiveObjState).
        /// </summary>
        public void WriteExplosiveState(PacketWriter w, Explodes ex)
        {
            ExplosiveObjState.Write(w, BreakSync.Activated(ex));
        }

        /// <summary>
        /// Client: the host's explosive state. An explosion here can't be
        /// taken back; the next state the host sends is how it is.
        /// </summary>
        public void ReadExplosiveState(PacketReader r)
        {
            ExplosiveObjState.Read(r);
        }
    }
}
