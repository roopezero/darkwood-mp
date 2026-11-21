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
        /// door, as this world is now. Refuses the request if not.
        /// </summary>
        internal bool Allowed(Action a, ObjRef obj, float x)
        {
            var door = ObjRef.Resolve<Door>(obj);
            if (door == null)
            {
                _coop.Authority.Refuse("no such door here");
                return false;
            }

            // A hit can come from a shot or our flamethrower's flame
            // (BreakSync.OwnFlame), which reach further than a hand.
            float doorReach;
            if (a == Action.DoorHit)
            {
                doorReach = WorldActions.ShotReach;
            }
            else
            {
                doorReach = WorldActions.Reach;
            }

            if (!_coop.Authority.InReach(
                door.transform.position, doorReach, door.name))
            {
                return false;
            }

            var locked = door.GetComponent<Locked>();
            bool isLocked = locked != null && locked.locked;
            switch (a)
            {
                case Action.DoorOpen when door.destroyed:
                    _coop.Authority.Refuse("the door is broken");
                    return false;
                case Action.DoorOpen when isLocked:
                    _coop.Authority.Refuse("the door is locked");
                    return false;
                case Action.DoorOpen when door.blocked:
                    _coop.Authority.Refuse("the door is blocked");
                    return false;
                // On a broken door the hammer's strokes rebuild it
                // (Door.setBarricadeState calls unDestroy): that's a
                // repair, allowed.
                case Action.DoorBarricade when door.barricaded
                    && !door.destroyed:
                    _coop.Authority.Refuse(
                        "the door is already barricaded");
                    return false;
                case Action.DoorDestroyBarricade when !door.barricaded:
                    _coop.Authority.Refuse(
                        "the door isn't barricaded");
                    return false;
                case Action.DoorHit when door.destroyed:
                    _coop.Authority.Refuse(
                        "the door is already broken");
                    return false;
                case Action.DoorUnlock when locked == null:
                    _coop.Authority.Refuse(
                        "the door has no lock to open");
                    return false;
                case Action.DoorUnlock when !locked.locked:
                    _coop.Authority.Refuse("the door isn't locked");
                    return false;
                case Action.DoorUnlock when x == 0f:
                    _coop.Authority.Refuse(WorldActions.StoryOnly);
                    return false;
                case Action.DoorLock:
                    _coop.Authority.Refuse(WorldActions.StoryOnly);
                    return false;
                case Action.DoorUnblock:
                    _coop.Authority.Refuse(WorldActions.StoryOnly);
                    return false;
                case Action.DoorUnlock
                    when x == 1f && !WorldActions.HasKey(locked):
                    _coop.Authority.Refuse(
                        $"nobody has the key ({locked.keyType})");
                    return false;
                case Action.DoorHit when x > 1000f:
                    _coop.Authority.Refuse(
                        $"a hit of {x:0} is more than any weapon does");
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Make a WorldAction's call on our door. False: it isn't
        /// here.
        /// </summary>
        internal bool Apply(Action a, ObjRef obj, in ActionValues values)
        {
            float x = values.X, y = values.Y;
            bool f1 = values.F1, f2 = values.F2;
            string str = values.Str;
            var door = WorldActions.Got<Door>(a, obj);
            if (door == null)
            {
                return false;
            }

            switch (a)
            {
                case Action.DoorOpen:
                    door.open(
                        new Vector3(x, door.transform.position.y, y),
                        null, 0f);
                    break;
                case Action.DoorClose:
                    door.close(null);
                    break;
                // As the hammer's third stroke: Door.barricade only
                // nails it up when barricadeState reaches 3 (and
                // rebuilds a broken door instead).
                case Action.DoorBarricade:
                    door.barricadeState = 2;
                    door.barricade(f1);
                    break;
                case Action.DoorDestroyBarricade:
                    door.destroyBarricade(f1);
                    break;
                case Action.DoorDestroy:
                    door.destroyDoor(f1);
                    break;
                case Action.DoorHit:
                    door.getHit((int)x, null, f1, f2);
                    break;
                case Action.DoorLock:
                    door.lockMe(str);
                    break;
                case Action.DoorUnlock:
                    door.unlock();
                    break;
                case Action.DoorUndestroy:
                    door.unDestroy();
                    break;
                case Action.DoorUnblock:
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

            var status = new DoorStatus
            {
                Opened = door.opened,
                Barricaded = door.barricaded,
                Destroyed = door.destroyed,
                Locked = l != null && l.locked,
            };

            return new HostDoor
            {
                Status = status,
                Blocked = door.blocked,
                Health = door.health,
                BarricadeHealth = door.barricadeHealth,
                KeyType = key,
            };
        }

        /// <summary>
        /// Client: put our door as the host has it.
        /// </summary>
        internal void RollBack(Door door, in HostDoor host)
        {
            bool open = host.Status.Opened,
                barricaded = host.Status.Barricaded,
                broken = host.Status.Destroyed,
                locked = host.Status.Locked;
            // only the story sets or clears it (Door.unblock), and only the
            // host runs that
            door.blocked = host.Blocked;
            if (broken && !door.destroyed)
            {
                // silently: a rollback, not the door breaking again
                door.destroyDoor(true);
            }
            else if (!broken && door.destroyed)
            {
                door.unDestroy();
            }

            var l = door.GetComponent<Locked>();
            if (locked && (l == null || !l.locked))
            {
                door.lockMe(host.KeyType);
            }
            else if (!locked && l != null && l.locked)
            {
                door.unlock();
            }

            // as in OnAction
            if (barricaded && !door.barricaded)
            {
                door.barricadeState = 2;
                door.barricade(false);
            }
            else if (!barricaded && door.barricaded)
            {
                door.destroyBarricade(false);
            }

            if (open && !door.opened)
            {
                door.open(
                    door.transform.position + door.transform.right, null, 0f);
            }
            else if (!open && door.opened)
            {
                door.close(null);
            }

            door.health = host.Health;
            door.barricadeHealth = host.BarricadeHealth;
        }
    }
}
