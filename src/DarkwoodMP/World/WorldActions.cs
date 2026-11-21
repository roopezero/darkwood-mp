using DarkwoodMP.World.Dreaming;
using DarkwoodMP.World.Inventories;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using DarkwoodMP.World.Scripted;
using DarkwoodMP.World.Windows;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Doors and windows, shared: when one is opened, closed, barricaded,
    /// damaged, broken, locked or unlocked in a player's game, the same call is
    /// made in everyone else's, so it looks and sounds as it does for them.
    ///
    /// WorldAction: u8 action, ObjRef, f32 a, f32 b, u8 flags, str s.
    /// A client's go to the host, which makes them and passes them on; the
    /// host's go to everyone. Only calls that changed something are sent
    /// (opening an open door, or trying a padlocked one, isn't).
    /// </summary>
    public static class WorldActions
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

        /// <summary>
        /// "--dwmp-debug-world": log every shared-world message sent and
        /// applied.
        /// </summary>
        public static bool Debug;

        private static Session S => Plugin.Instance.Host.Session;

        public static void Register(Session s)
        {
            s.Register(MsgType.WorldAction, OnAction);
            s.Register(MsgType.ObjState, OnObjState);
        }

        /// <summary>
        /// How far from a thing a player may be to act on it (generous:
        /// positions arrive a little late).
        /// </summary>
        public const float Reach = 600f;
        /// <summary>
        /// How far a player may be from what they shot (the game's shots reach
        /// 1000 px), with the same margin.
        /// </summary>
        public const float ShotReach = 1200f;

        /// <summary>
        /// What a client sends is what its player did. The world also changes
        /// by itself (scripts that barricade a house at dusk, enemies breaking
        /// in): that's the host's to say, and its game does it there too.
        /// </summary>
        private static readonly
            System.Collections.Generic.HashSet<Type> PlayerSide =
                new System.Collections.Generic.HashSet<Type>
            {
                typeof(Player), typeof(InputScript), typeof(InvSlot),
                typeof(InventoryController), typeof(ConstructionMenu),
                typeof(Padlock), typeof(global::Button),
            };

        /// <summary>
        /// Tests only: count what's done now as our own player's doing.
        /// </summary>
        public static bool TestAsPlayer;

        public static bool ByLocalPlayer()
        {
            if (TestAsPlayer)
            {
                return true;
            }

            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 24; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                Type t = null;
                if (frame != null)
                {
                    MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        t = method.DeclaringType;
                    }
                }

                // lambdas/iterators are nested types
                for (; t != null; t = t.DeclaringType)
                {
                    if (PlayerSide.Contains(t))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Client: our player is somewhere the host doesn't run: a dream of our
        /// own (not the shared one, DreamSync.Shared), also while going into
        /// it, or a separate location the host didn't place
        /// (Whereabouts.HostRunsHere). What happens there is ours alone, as in
        /// single player.
        /// </summary>
        public static bool InOwnArea()
        {
            var s = S;
            if (s == null || !s.IsClient)
            {
                return false;
            }

            var d = Singleton<Dreams>.Instance;
            if (d != null && DreamSync.SharedNow == null
                && (d.dreaming || d.dreamPrepared || d.switchingDream))
            {
                return true;
            }

            return !Places.Whereabouts.HostRunsHere();
        }

        /// <summary>
        /// Client: this thing is in an area only our player is in (InOwnArea,
        /// or NetEnemies.LocalOnly: a dream's place of our own, a scene of its
        /// own). The host doesn't have it, so asking the host about it is only
        /// refused: our player's doing there runs here alone (Part.All), as in
        /// single player, and isn't sent. A shared dream's things (the level
        /// dream, a prologue stage) are the host's to decide, as the world's
        /// are. Null: only where our player is counts.
        /// </summary>
        public static bool OursAlone(GameObject go)
        {
            var s = S;
            if (s == null || !s.IsClient)
            {
                return false;
            }

            if (go == null)
            {
                return InOwnArea();
            }

            var d = Singleton<Dreams>.Instance;
            if (DreamSync.SharedNow != null && d != null
                && d.dreamLocation != null
                && (d.dreaming || d.dreamPrepared || d.switchingDream)
                && (d.preset == null || d.preset.name == DreamSync.SharedNow)
                && go.transform.IsChildOf(d.dreamLocation.transform))
            {
                return false;
            }

            return NetEnemies.LocalOnly(go) || InOwnArea();
        }

        public static void Send(
            Action a, GameObject go, float x = 0, float y = 0, bool f1 = false,
            bool f2 = false, string str = "", bool byPlayerKnown = false)
        {
            if (S.IsClient && !byPlayerKnown && !ByLocalPlayer())
            {
                return;
            }

            if (OursAlone(go))
            {
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {a} on {go.name}: ours alone (not where the "
                            + "host runs), not sent");
                }

                return;
            }

            // The host's player's own: the triggers it set off here are sent as
            // our player's, so the joiners leave them to that.
            bool byPlayer = S.IsHost && SetsOffTriggers(a)
                && Coop.Current.Scripted.OurPlayersAction();
            Send(a, ObjRef.Of(go), go.name, x, y, f1, f2, str, byPlayer);
        }

        /// <summary>
        /// Actions whose replay can set off a trigger that the player who did
        /// it sends (ScriptedEvents.ActionTypes).
        /// </summary>
        private static bool SetsOffTriggers(Action a) => a == Action.DoorOpen
            || a == Action.DoorClose || a == Action.ItemOn
            || a == Action.ItemOff;

        /// <summary>
        /// Send an action on a thing named before the call (it may be gone by
        /// now, like a barrel that blew up).
        /// The caller has checked that it's ours to send.
        /// </summary>
        /// <param name="byPlayer">Host: our player did it (flags 4: the
        /// triggers it sets off come from us as our player's).</param>
        public static void Send(
            Action a, ObjRef r, string name, float x = 0, float y = 0,
            bool f1 = false, bool f2 = false, string str = "",
            bool byPlayer = false)
        {
            if (!r.Valid)
            {
                return;
            }

            if (S.IsClient)
            {
                GameObject go = null;
                if (ObjRef.Resolve<Transform>(r) is Transform t && t != null)
                {
                    go = t.gameObject;
                }

                if (OursAlone(go))
                {
                    if (Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] {a} {r} ({name}): ours alone, not sent");
                    }

                    return;
                }
            }

            if (S.IsClient && HammerStroke.Depth > 0)
            {
                HammerStroke.Held.Add(
                    () => Send(a, r, name, x, y, f1, f2, str));
                return;
            }

            var w = S.Begin(MsgType.WorldAction);
            WriteAction(w, a, r, x, y, f1, f2, str, byPlayer && S.IsHost);
            if (Debug)
            {
                Plugin.Log.LogInfo($"[world] sent {a} {r} ({name})");
            }

            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                Coop.Current.Authority.Request(w, $"{a} {r} ({name})");
            }
        }

        /// <param name="byPlayer">Flags 4, host to joiners: a player did it (a
        /// joiner's action passed on, or the host's player's), and sends the
        /// triggers it sets off itself, so applying it here doesn't set them
        /// off.</param>
        internal static void WriteAction(
            PacketWriter w, Action a, ObjRef r, float x, float y, bool f1,
            bool f2, string str, bool byPlayer = false)
        {
            byte flags = 0;
            if (f1)
            {
                flags |= WorldActionBody.First;
            }

            if (f2)
            {
                flags |= WorldActionBody.Second;
            }

            if (byPlayer)
            {
                flags |= WorldActionBody.ByPlayer;
            }

            var body = new WorldActionBody
            {
                Action = (byte)a,
                Obj = ObjRef.NameOf(r),
                X = x,
                Y = y,
                Flags = flags,
                Text = str,
            };

            WorldActionMessage.Write(w, body);
        }

        private static void OnAction(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            WorldActionBody body = WorldActionMessage.Read(r);
            var a = (Action)body.Action;
            var obj = ObjRef.RefOf(body.Obj);
            float x = body.X, y = body.Y;
            string str = body.Text;
            bool f1 = body.Has(WorldActionBody.First);
            bool f2 = body.Has(WorldActionBody.Second);
            var values = new ActionValues
            {
                X = x,
                Y = y,
                F1 = f1,
                F2 = f2,
                Str = str,
            };

            // A joiner's action (to the host), or one the host says a player
            // did: that player sets off its triggers.
            bool byPlayer = s.IsHost || body.Has(WorldActionBody.ByPlayer);

            if (s.IsHost && Coop.Current.Authority.Handling
                && !Allowed(a, obj, x, y, f1, str))
            {
                // A player in a dream of their own (or a place the host doesn't
                // have) acted on their own copy of it: not the host's to
                // refuse, nor to apply. Their game no longer sends these
                // (OursAlone); this is for one that was on the way as they
                // went.
                if (Coop.Current.Authority.RequesterAway)
                {
                    Coop.Current.Authority.AcceptAway($"{a} {obj}");
                    return;
                }

                SendState(
                    from, obj, padlock: a == Action.PadlockUnlock,
                    goneMeansGone: GoneMeansGone(a));
                return;
            }

            // Applied as outside any explosion or flame, whatever this frame
            // was in when the message was read: on a client a hit inside one is
            // left to the host (BreakSync.HostsHit), and this is the host's
            // hit.
            int thrownDepth = ThrownHits.Depth;
            var flame = ThrownHits.CurrentFlame;
            ThrownHits.Depth = 0;
            ThrownHits.CurrentFlame = null;
            Coop.Guards.Replaying = true;
            var scripted = Coop.Current.Scripted;
            var leftWas = scripted.LeftToActor;
            if (byPlayer)
            {
                scripted.LeftToActor = ScriptedEvents.ActionTypes;
            }

            // A world trigger this sets off (an NPC's or item's onDeath, an
            // item switched on) is the sender's doing.
            var wasCause = scripted.Cause;
            if (s.IsHost)
            {
                scripted.Cause = from;
            }

            try
            {
                if (a == Action.TrapDisarm
                    && Crate(ObjRef.Resolve<Item>(obj)) is Item crate)
                {
                    // A locked crate: the host rolls the pick
                    // (LockShare.Pick), passed on as ItemSearched if it
                    // worked.
                    if (!Coop.Current.Locks.Pick(from, obj, crate))
                    {
                        return;
                    }

                    a = Action.ItemSearched;
                }
                else if (a == Action.TrapDisarm)
                {
                    // Only ever a request to the host: rolled here
                    // (TrapDisarms.Roll). The trap goes for the others as
                    // TrapSwitched.
                    Coop.Current.Traps.Disarms.Roll(from, obj);
                    a = Action.TrapSwitched;
                }
                else if (ObjectActions.Handles(a))
                {
                    Coop.Current.Objects.Apply(a, obj, ref values);
                    y = values.Y;
                }
                else if (a == Action.Explode)
                {
                    if (!Coop.Current.Breaks.Explode(obj, f1))
                    {
                        return;
                    }
                }
                else if (a >= Action.ItemHit)
                {
                    if (!Coop.Current.Breaks.Apply(a, obj, ref values))
                    {
                        return;
                    }

                    y = values.Y;
                }
                else if (a == Action.NpcDied)
                {
                    Coop.Current.NpcDeaths.Apply(a, obj, body);
                }
                else if (a == Action.ItemUnlock || a == Action.ItemSearched
                    || a == Action.PadlockUnlock)
                {
                    if (!Coop.Current.Locks.Apply(a, obj))
                    {
                        return;
                    }
                }
                else if (a >= Action.ItemOn)
                {
                    if (!Coop.Current.Switches.Apply(a, obj))
                    {
                        return;
                    }
                }
                else if (a >= Action.TrapFired)
                {
                    if (!Coop.Current.Traps.Apply(a, obj))
                    {
                        return;
                    }
                }
                else if (a < Action.WindowBarricade)
                {
                    if (!Coop.Current.Doors.Apply(a, obj, values))
                    {
                        return;
                    }
                }
                else if (!Coop.Current.Windows.Apply(a, obj, values))
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't apply {a} to {obj}: {e.Message}");
            }
            finally
            {
                Coop.Guards.Replaying = false;
                scripted.LeftToActor = leftWas;
                ThrownHits.Depth = thrownDepth;
                ThrownHits.CurrentFlame = flame;
                scripted.Cause = wasCause;
            }

            if (s.IsHost)
            {
                Coop.Current.Objects.AfterOnHost(a, obj, f1);
                // The host passes a client's action on to the others.
                var w = s.Begin(MsgType.WorldAction);
                WriteAction(w, a, obj, x, y, f1, f2, str, byPlayer: true);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        /// <summary>
        /// Resolve the thing an action names, logging it with
        /// --dwmp-debug-world.
        /// </summary>
        internal static T Got<T>(Action a, ObjRef obj) where T : Component =>
            Logged(a, obj, ObjRef.Resolve<T>(obj));

        internal static T Logged<T>(Action a, ObjRef obj, T found)
            where T : Component
        {
            if (Debug)
            {
                string name = "not found here";
                if (found != null)
                {
                    name = found.name;
                }

                Plugin.Log.LogInfo($"[world] got {a} {obj}: " + name);
            }

            return found;
        }

        // ---- the host's checks ----------------------------------------------

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

        // ---- the state to roll back to --------------------------------------

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

    // ---- the calls ----------------------------------------------------------

    /// <summary>
    /// A hammer stroke (Player.checkFrameTrigger "HammerWork"): the stroke that
    /// nails up a barricade or rebuilds a broken door calls Door.barricade (or
    /// Window.barricade) first and takes the wood and nails after it. A client
    /// sends that action as a request once the call is over, so the request
    /// carries the materials it used up and a refused one gives them back
    /// (InventoryWatch).
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.checkFrameTrigger))]
    static class HammerStroke
    {
        internal static int Depth;
        internal static readonly System.Collections.Generic.List<System.Action>
            Held = new System.Collections.Generic.List<System.Action>();

        static void Prefix() => Depth++;
        static void Finalizer()
        {
            if (--Depth > 0)
            {
                return;
            }

            Depth = 0;
            if (Held.Count == 0)
            {
                return;
            }

            var held = Held.ToArray();
            Held.Clear();
            foreach (var send in held)
            {
                try
                {
                    send();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't send a hammer stroke's action: {e.Message}");
                }
            }
        }
    }
}
