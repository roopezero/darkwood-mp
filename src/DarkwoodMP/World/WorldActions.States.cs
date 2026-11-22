using DarkwoodMP.World.Inventories;
using System;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using DarkwoodMP.World.Scripted;
using DarkwoodMP.World.Windows;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// WorldActions' ObjState: the state a refused request rolls back to.
    public static partial class WorldActions
    {
        private enum Kind : byte
        {
            Gone = 0,
            Door = 1,
            Window = 2,
            Trap = 3,
            Item = 4,
            Padlock = 5,
            Explosive = 6
        }

        /// <summary>
        /// Host: tell a client how this thing really is (ObjState: ObjRef,
        /// u8 kind, then the kind's state), so it can put its copy back after a
        /// refused request.
        /// door: u8 flags (1 open, 2 barricaded, 4 broken, 8 locked, 16
        /// blocked), i32 health, i32 barricade health, str key type.
        /// window: u8 flags (1 barricaded), i32 barricade health. trap:
        /// u8 flags (1 armed).
        /// item: u8 flags (1 switched on, 2 locked, 4 searched, 8 broken, 16
        /// its InteractiveItem is on, 32 its feeder is full), i32 health.
        /// padlock (when the request was about the padlock): u8 flags (1
        /// locked). explosive (an Explodes that isn't an item): u8 flags (1
        /// gone off).
        /// gone: nothing (it isn't in the host's world; sent only when the
        /// request was one after which a thing goes, GoneMeansGone).
        /// </summary>
        public static void SendState(
            ulong to, ObjRef obj, bool padlock = false,
            bool goneMeansGone = true)
        {
            var go = ObjRef.Resolve<Transform>(obj);
            // Not found here: only after a pickup, a break or a trap (things
            // that go) does that mean the client's copy should go too. A door,
            // a lock or a switch the host hasn't got is one the client has and
            // the host doesn't (a place of its own): nothing to roll it back
            // to, and it mustn't be destroyed.
            if (go == null && !goneMeansGone)
            {
                return;
            }

            var w = S.Begin(MsgType.ObjState);
            obj.Write(w);
            Door door = null;
            if (go != null)
            {
                door = go.GetComponent<Door>();
            }

            Window window = null;
            if (go != null)
            {
                window = go.GetComponent<Window>();
            }

            Trigger trap = null;
            if (go != null)
            {
                trap = go.GetComponent<Trigger>();
            }

            Item item = null;
            if (go != null)
            {
                item = go.GetComponent<Item>();
            }

            Padlock pad = null;
            if (padlock && go != null)
            {
                pad = go.GetComponent<Padlock>();
            }

            if (pad != null)
            {
                w.W.Write((byte)Kind.Padlock);
                Coop.Current.Locks.WriteState(w, pad);
            }
            else if (door != null)
            {
                w.W.Write((byte)Kind.Door);
                DoorObjState.Write(w, Coop.Current.Doors.HostOf(door));
            }
            else if (window != null)
            {
                w.W.Write((byte)Kind.Window);
                WindowObjState.Write(w, Coop.Current.Windows.HostOf(window));
            }
            else if (trap != null)
            {
                w.W.Write((byte)Kind.Trap);
                Coop.Current.Traps.WriteState(w, trap);
            }
            else if (item != null)
            {
                w.W.Write((byte)Kind.Item);
                Coop.Current.Breaks.WriteState(w, item);
            }
            else if (go != null && go.GetComponent<Explodes>() != null)
            {
                w.W.Write((byte)Kind.Explosive);
                Coop.Current.Breaks.WriteExplosiveState(
                    w, go.GetComponent<Explodes>());
            }
            else
            {
                w.W.Write((byte)Kind.Gone);
            }

            S.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Whether a thing a request was about that the host can't find is gone
        /// for good (Kind.Gone removes the requester's copy): taken off the
        /// ground, broken, blown up, or a trap set off, disarmed or taken.
        /// </summary>
        internal static bool GoneMeansGone(Action a) =>
            a == Action.Removed || a == Action.ItemHit
                || a == Action.ItemBroken || a == Action.Explode
                || a == Action.TrapFired || a == Action.TrapSwitched
                || a == Action.TrapDisarm;

        /// <summary>
        /// Client: put our copy of this thing as the host has it (a refused
        /// request's rollback).
        /// </summary>
        private static void OnObjState(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId
                || !WorldShare.InWorld)
            {
                return;
            }

            var obj = ObjRef.Read(r);
            var kind = (Kind)r.R.ReadByte();
            var go = ObjRef.Resolve<Transform>(obj);
            Coop.Guards.Replaying = true;
            // A rollback puts the thing as it is, it isn't anyone doing it: the
            // triggers it would set off aren't.
            var scripted = Coop.Current.Scripted;
            var leftWas = scripted.LeftToActor;
            scripted.LeftToActor = ScriptedEvents.ActionTypes;
            try
            {
                switch (kind)
                {
                    case Kind.Gone:
                        if (go == null
                            || (go.GetComponent<Trigger>() == null
                                && go.GetComponent<Item>() == null))
                        {
                            break;
                        }

                        // Ours alone (a dream of our own): the host not having
                        // it says nothing about ours.
                        if (OursAlone(go.gameObject))
                        {
                            Plugin.Log.LogInfo(
                                $"[world] the host hasn't got {go.name} {obj}: "
                                    + "ours alone, kept");
                            break;
                        }

                        Plugin.Log.LogInfo(
                            $"[world] {go.name} {obj} is gone on the host: "
                                + "removed ours");
                        Containers.CloseIfOpen(go.gameObject);
                        UnityEngine.Object.Destroy(go.gameObject);
                        break;
                    case Kind.Door:
                        {
                            var host = DoorObjState.Read(r);
                            Door door = null;
                            if (go != null)
                            {
                                door = go.GetComponent<Door>();
                            }

                            if (door != null)
                            {
                                Coop.Current.Doors.RollBack(door, host);
                            }

                            break;
                        }

                    case Kind.Window:
                        {
                            var host = WindowObjState.Read(r);
                            Window window = null;
                            if (go != null)
                            {
                                window = go.GetComponent<Window>();
                            }

                            if (window != null)
                            {
                                Coop.Current.Windows.RollBack(window, host);
                            }

                            break;
                        }

                    case Kind.Trap:
                        Coop.Current.Traps.ReadState(r, go);
                        break;

                    case Kind.Item:
                        Coop.Current.Breaks.ReadState(r, go);
                        break;

                    case Kind.Explosive:
                        Coop.Current.Breaks.ReadExplosiveState(r);
                        break;

                    case Kind.Padlock:
                        Coop.Current.Locks.ReadState(r, go);
                        break;
                }

                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] the host's state of {obj} ({kind}) applied");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't apply the host's state of {obj}: {e.Message}");
            }
            finally
            {
                Coop.Guards.Replaying = false;
                scripted.LeftToActor = leftWas;
            }
        }
    }
}
