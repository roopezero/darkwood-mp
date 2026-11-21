using System;
using DarkwoodMP.World.Doors;
using DarkwoodMP.World.Windows;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// WorldActions' list of what was done (a WorldAction's u8 action).
    public static partial class WorldActions
    {
        public enum Action : byte
        {
            DoorOpen = 1, DoorClose = 2, DoorBarricade = 3,
            DoorDestroyBarricade = 4, DoorDestroy = 5, DoorHit = 6,
            /// <summary>
            /// DoorLock: s the key type. DoorUnlock: a is 0 by the story, 1
            /// with a key, 2 with a lockpick (Locked.unlock).
            /// </summary>
            DoorLock = 7, DoorUnlock = 8, DoorUndestroy = 9,
            /// <summary>
            /// Host to clients: the story cleared the door's "blocked"
            /// (GameEvent modifyDoor unblock, Door.unblock), so a player can
            /// open it now.
            /// </summary>
            DoorUnblock = 10,
            WindowBarricade = 11, WindowDestroyBarricade = 12, WindowHit = 13,
            TrapFired = 21, TrapSwitched = 22,
            /// <summary>
            /// Client to host: our player finished disarming this trap, or
            /// picking this locked crate; the host rolls whether it worked
            /// (Verdict result "1" or "0").
            /// </summary>
            TrapDisarm = 23,
            ItemOn = 31, ItemOff = 32,
            /// <summary>
            /// A lock on something that isn't a door (a chest, a cabinet) was
            /// opened. a: 0 by the story, 1 with a key, 2 with a lockpick.
            /// </summary>
            ItemUnlock = 33,
            /// <summary>
            /// Something with an inventory is searched now (Item.searched, and
            /// Character.searched on a corpse), which saves and shows
            /// "(searched)" on hover. Host to clients: a locked crate
            /// (Item.disarmTime &gt; 0) was picked open, so it opens without
            /// the progress bar, or the host's player opened a container.
            /// Client to host: our player opened a container
            /// (Item.openInventory); never a crate that isn't picked open yet.
            /// </summary>
            ItemSearched = 34,
            /// <summary>
            /// A combination padlock was opened. flag 1: by a player who
            /// entered the code (s: the code they entered).
            /// </summary>
            PadlockUnlock = 35,
            NpcDied = 41,
            /// <summary>
            /// Furniture or another destructible item took a hit: a = damage,
            /// b = its health after it, f1 = normalHit.
            /// </summary>
            ItemHit = 51,
            /// <summary>
            /// A destructible item broke without a hit (Item.die called by
            /// itself).
            /// </summary>
            ItemBroken = 52,
            /// <summary>
            /// A broken item was repaired (Item.setNormalState, from building).
            /// </summary>
            ItemRepaired = 53,
            /// <summary>
            /// An exploding barrel or gas bottle went off
            /// (Explodes.onActivate): f1 = flaming.
            /// </summary>
            Explode = 61,
            /// <summary>
            /// An InteractiveItem (a switch shown in its own window) was
            /// switched. flag 1: it's on now (World.ObjectActions).
            /// </summary>
            InteractiveSwitch = 90,
            /// <summary>
            /// Something was taken off the ground or eaten: a note picked up, a
            /// death bag's experience taken, a lure the host's animals ate. The
            /// others remove their copy (World.ObjectActions).
            /// </summary>
            Removed = 91,
            /// <summary>
            /// A feeder was used: it's empty until the next dawn, for everyone
            /// (World.ObjectActions). A client's waits for the host's yes.
            /// </summary>
            FeederUsed = 92,
            /// <summary>
            /// Spilt gasoline (a Liquid: a trail or a puddle) caught fire. Host
            /// to clients: where the fire started (it spreads by itself in each
            /// game). Client to host: our player's burning weapon lit it
            /// (World.ObjectActions).
            /// </summary>
            LiquidLit = 93,
            /// <summary>
            /// A vine (ChainParent) was cut at: x = damage, y = its health
            /// after the hit (World.ObjectActions). Detaching at 0 follows in
            /// each game.
            /// </summary>
            VineHit = 130,
            /// <summary>
            /// Host to clients: the shadow covering an item (ShadowArmor) died,
            /// in light or from hits (World.ObjectActions).
            /// </summary>
            ShadowArmorDied = 131,
        }
    }
}
