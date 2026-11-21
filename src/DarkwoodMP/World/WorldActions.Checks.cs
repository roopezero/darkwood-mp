using System;
using System.Collections.Generic;
using DarkwoodMP.World.Doors;
using DarkwoodMP.World.Windows;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// WorldActions' checks: whether the host lets a client's action be.
    public static partial class WorldActions
    {
        /// <summary>
        /// Locking a door, and opening a lock or a padlock other than with a
        /// key, a lockpick or the code, is only done by a scripted event's door
        /// steps (GameEvent modifyDoor and unlockDoor), which are the host's
        /// part (ScriptedEvents.HostSteps) and never run in a client's game for
        /// a place the host has. A player has no way to.
        /// </summary>
        internal const string StoryOnly =
            "only the story does that, in the host's game";

        /// <summary>
        /// Host: whether a client's player could have done this, as this world
        /// is now: the thing is here, they're near it, and it's in a state the
        /// action applies to. Refuses the request if not.
        /// </summary>
        private static bool Allowed(
            Action a, ObjRef obj, float x, float y, bool f1, string str)
        {
            if (ObjectActions.Handles(a))
            {
                var values = new ActionValues { X = x, F1 = f1 };
                return Coop.Current.Objects.Allowed(a, obj, values);
            }

            if (a == Action.Explode)
            {
                return Coop.Current.Breaks.MayExplode(obj);
            }

            if (a >= Action.ItemHit)
            {
                return Coop.Current.Breaks.MayChange(a, obj, x);
            }

            if (a == Action.NpcDied)
            {
                return Coop.Current.NpcDeaths.MayDie(
                    obj, str, new Vec2(x, y));
            }

            if (a == Action.ItemUnlock)
            {
                return Coop.Current.Locks.MayUnlock(obj, x);
            }

            if (a == Action.ItemSearched)
            {
                return Coop.Current.Locks.MaySearch(obj);
            }

            if (a == Action.PadlockUnlock)
            {
                return Coop.Current.Locks.MayUnlockPadlock(obj, f1, str);
            }

            if (a == Action.TrapDisarm
                && ObjRef.Resolve<Item>(obj) is Item picked
                && picked.GetComponent<Trigger>() == null)
            {
                return Coop.Current.Locks.MayPick(picked);
            }

            if (a >= Action.ItemOn)
            {
                return Coop.Current.Switches.MayTurn(obj);
            }

            if (a >= Action.TrapFired)
            {
                return Coop.Current.Traps.Allowed(a, obj);
            }

            if (a < Action.WindowBarricade)
            {
                return Coop.Current.Doors.Allowed(a, obj, x);
            }

            return Coop.Current.Windows.Allowed(a, obj, x);
        }

        /// <summary>
        /// Host: whether the lock's key is in the journal. Keys are journal
        /// entries, shared by FlagsSync (a key found in a shared dream too,
        /// such as the prologue's), so a key a client's player picked up is in
        /// the host's journal too: the client sends its journal changes and the
        /// key just before the unlock (LockShare.Send, FlagsSync.SendKey), on
        /// the same ordered channel.
        /// </summary>
        public static bool HasKey(Locked l)
        {
            UI instance = Singleton<UI>.Instance;
            Dictionary<string, Journal.Key> keys = null;
            if (instance != null)
            {
                Journal journal = instance.journal;
                if (journal != null)
                {
                    keys = journal.keysDict;
                }
            }

            return l != null && keys != null
                && !string.IsNullOrEmpty(l.keyType)
                && keys.ContainsKey(l.keyType);
        }

        /// <summary>
        /// The squared distance between two points on the ground (x and z; y is
        /// ignored).
        /// </summary>
        internal static float FlatSq(Vector3 a, Vector3 b) =>
            (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

        /// <summary>
        /// A crate whose lock is picked with the progress bar (Item.disarm
        /// without a Trigger), or null.
        /// </summary>
        public static Item Crate(Item item)
        {
            if (item != null && World.Locks.LockRules.IsCrate(
                item.disarmTime, item.GetComponent<Inventory>() != null,
                item.GetComponent<Trigger>() != null))
            {
                return item;
            }

            return null;
        }
    }
}
