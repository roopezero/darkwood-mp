using DarkwoodMP.Net;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World.Doors
{
    /// <summary>
    /// Doors, shared: when one is opened, closed, barricaded, damaged,
    /// broken, locked or unlocked in a player's game, the same call is made
    /// in everyone else's (WorldActions sends and reads the WorldAction).
    /// </summary>
    internal sealed class Doors
    {
        private readonly CoopSession _coop;

        public Doors(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// Host: whether a client's player could have done this to the
        /// door, as this world is now (DoorRules.MayAct). Refuses the
        /// request if not.
        /// </summary>
        internal bool Allowed(Action a, ObjRef obj, float x)
        {
            var facts = FactsOf(ObjRef.Resolve<Door>(obj));
            var verdict = DoorRules.MayAct(facts, (DoorAction)a, x);
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the host's check knows of a door and the requesting player:
        /// the only code that reads a door for DoorRules. door may be null
        /// (not in this world).
        /// </summary>
        private DoorFacts FactsOf(Door door)
        {
            if (door == null)
            {
                return new DoorFacts();
            }

            var reach = _coop.Authority.ReachOf(
                door.transform.position,
                door.name);
            var facts = new DoorFacts
            {
                Exists = true,
                Requester = reach,
                Destroyed = door.destroyed,
                Barricaded = door.barricaded,
                Blocked = door.blocked,
            };

            var locked = door.GetComponent<Locked>();
            if (locked != null)
            {
                facts = facts with
                {
                    HasLock = true,
                    Locked = locked.locked,
                    HasKey = WorldActions.HasKey(locked),
                    KeyType = locked.keyType,
                };
            }

            return facts;
        }

        /// <summary>
        /// Make a WorldAction's call on our door. False: it isn't here.
        /// </summary>
        internal bool Apply(Action a, ObjRef obj, in ActionValues values)
        {
            var door = WorldActions.Got<Door>(a, obj);
            if (door == null)
            {
                return false;
            }

            switch ((DoorAction)a)
            {
                case DoorAction.Open:
                    var at = new Vector3(
                        values.X,
                        door.transform.position.y,
                        values.Y);
                    door.open(at, null, 0f);
                    break;
                case DoorAction.Close:
                    door.close(null);
                    break;
                // As the hammer's third stroke: Door.barricade only nails it
                // up when barricadeState reaches 3 (and rebuilds a broken
                // door instead).
                case DoorAction.Barricade:
                    door.barricadeState = 2;
                    door.barricade(values.F1);
                    break;
                case DoorAction.DestroyBarricade:
                    door.destroyBarricade(values.F1);
                    break;
                case DoorAction.Destroy:
                    door.destroyDoor(values.F1);
                    break;
                case DoorAction.Hit:
                    door.getHit((int)values.X, null, values.F1, values.F2);
                    break;
                case DoorAction.Lock:
                    door.lockMe(values.Str);
                    break;
                case DoorAction.Unlock:
                    door.unlock();
                    break;
                case DoorAction.Undestroy:
                    door.unDestroy();
                    break;
                case DoorAction.Unblock:
                    door.unblock();
                    break;
            }

            return true;
        }

        /// <summary>
        /// The door as the host has it, for an ObjState.
        /// </summary>
        internal HostDoor HostOf(Door door)
        {
            var l = door.GetComponent<Locked>();
            string key = "";
            if (l != null)
            {
                key = l.keyType;
            }

            return new HostDoor
            {
                Status = StatusOf(door),
                Blocked = door.blocked,
                Health = door.health,
                BarricadeHealth = door.barricadeHealth,
                KeyType = key,
            };
        }

        /// <summary>
        /// Client: put our door as the host has it (DoorRules.RollBack).
        /// Each step looks at the door as the one before left it: breaking
        /// a door can take its barricade and open it.
        /// </summary>
        internal void RollBack(Door door, in HostDoor host)
        {
            // only the story sets or clears it (Door.unblock), and only the
            // host runs that
            door.blocked = host.Blocked;
            var fixes = DoorRules.RollBack(host.Status, StatusOf(door));
            if ((fixes & DoorFixes.Break) != 0)
            {
                // silently: a rollback, not the door breaking again
                door.destroyDoor(true);
            }
            else if ((fixes & DoorFixes.Mend) != 0)
            {
                door.unDestroy();
            }

            fixes = DoorRules.RollBack(host.Status, StatusOf(door));
            if ((fixes & DoorFixes.Lock) != 0)
            {
                door.lockMe(host.KeyType);
            }
            else if ((fixes & DoorFixes.Unlock) != 0)
            {
                door.unlock();
            }

            // as in Apply
            fixes = DoorRules.RollBack(host.Status, StatusOf(door));
            if ((fixes & DoorFixes.Barricade) != 0)
            {
                door.barricadeState = 2;
                door.barricade(false);
            }
            else if ((fixes & DoorFixes.RemoveBarricade) != 0)
            {
                door.destroyBarricade(false);
            }

            fixes = DoorRules.RollBack(host.Status, StatusOf(door));
            if ((fixes & DoorFixes.Open) != 0)
            {
                door.open(
                    door.transform.position + door.transform.right, null, 0f);
            }
            else if ((fixes & DoorFixes.Close) != 0)
            {
                door.close(null);
            }

            door.health = host.Health;
            door.barricadeHealth = host.BarricadeHealth;
        }

        /// <summary>
        /// The parts of a door a rollback compares.
        /// </summary>
        private static DoorStatus StatusOf(Door door)
        {
            var l = door.GetComponent<Locked>();
            return new DoorStatus
            {
                Opened = door.opened,
                Barricaded = door.barricaded,
                Destroyed = door.destroyed,
                Locked = l != null && l.locked,
            };
        }
    }
}
