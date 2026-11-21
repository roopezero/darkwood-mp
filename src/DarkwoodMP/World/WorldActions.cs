using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
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

        private static Session S => Session.Instance;

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
        /// (Locations.HostRunsHere). What happens there is ours alone, as in
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
            if (d != null && DreamSync.Shared == null
                && (d.dreaming || d.dreamPrepared || d.switchingDream))
            {
                return true;
            }

            return !Locations.HostRunsHere();
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
            if (DreamSync.Shared != null && d != null
                && d.dreamLocation != null
                && (d.dreaming || d.dreamPrepared || d.switchingDream)
                && (d.preset == null || d.preset.name == DreamSync.Shared)
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
                && ScriptedEvents.OurPlayersAction();
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
            var leftWas = ScriptedEvents.LeftToActor;
            if (byPlayer)
            {
                ScriptedEvents.LeftToActor = ScriptedEvents.ActionTypes;
            }

            // A world trigger this sets off (an NPC's or item's onDeath, an
            // item switched on) is the sender's doing.
            var wasCause = ScriptedEvents.Cause;
            if (s.IsHost)
            {
                ScriptedEvents.Cause = from;
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
                    ObjectActions.Apply(a, obj, f1, x, ref y);
                }
                else if (a == Action.Explode)
                {
                    var ex = Got<Explodes>(a, obj);
                    if (ex == null || ex.GetComponent<Character>() != null)
                    {
                        return; // never an enemy's (see BreakSync)
                    }

                    if (!BreakSync.Activated(ex))
                    {
                        ex.onActivate(f1);
                    }
                }
                else if (a >= Action.ItemHit)
                {
                    var item = Got<Item>(a, obj);
                    if (item == null)
                    {
                        return;
                    }

                    switch (a)
                    {
                        case Action.ItemHit:
                            if (item.destroyed)
                            {
                                break;
                            }

                            // A client's hit is applied to the host's health;
                            // the host's own says what health it left, so a
                            // copy that missed a hit (or took one more) comes
                            // back in step.
                            if (!s.IsHost && item.shadowArmor == null)
                            {
                                // a shadow takes the hit, not the item
                                item.health = (int)y + (int)x;
                            }

                            item.getHit((int)x, null, f1);
                            if (s.IsHost)
                            {
                                y = item.health;
                            }

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
                }
                else if (a == Action.NpcDied)
                {
                    var npc = Logged(a, obj, DyingNpc(obj, str, x, y));
                    // A copy of an NPC the host runs as an enemy dies with the
                    // host's state (Combat.NetEnemies).
                    bool puppet = npc != null
                        && Combat.NetEnemies.Instance != null
                        && Combat.NetEnemies.Instance.IsPuppet(npc, out _);
                    if (npc != null && npc.alive && !puppet)
                    {
                        npc.die();
                    }
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
                    var item = Got<Item>(a, obj);
                    if (item == null)
                    {
                        return;
                    }

                    if (a == Action.ItemOn)
                    {
                        item.turnOn();
                    }
                    else
                    {
                        item.turnOff();
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
                    var door = Got<Door>(a, obj);
                    if (door == null)
                    {
                        return;
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
                }
                else
                {
                    var window = Got<Window>(a, obj);
                    if (window == null)
                    {
                        return;
                    }

                    switch (a)
                    {
                        case Action.WindowBarricade:
                            // as for a door
                            window.barricadeState = 2;
                            window.barricade((int)x, f1);
                            break;
                        case Action.WindowDestroyBarricade:
                            window.destroyBarricade(f1);
                            break;
                        case Action.WindowHit:
                            window.getHit((int)x, null, f1);
                            break;
                    }
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
                ScriptedEvents.LeftToActor = leftWas;
                ThrownHits.Depth = thrownDepth;
                ThrownHits.CurrentFlame = flame;
                ScriptedEvents.Cause = wasCause;
            }

            if (s.IsHost)
            {
                ObjectActions.AfterOnHost(a, obj, f1);
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

        private static T Logged<T>(Action a, ObjRef obj, T found)
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
        /// The NPC an NpcDied names: by its ref, else (an NPC each game placed
        /// itself, or one that walked off its named spot) the living NPC of
        /// that name nearest to where it died in the killer's game, within 400.
        /// </summary>
        private static Character DyingNpc(
            ObjRef obj, string name, float x, float z)
        {
            var c = ObjRef.Resolve<Character>(obj);
            if (c != null && c.npc != null
                && (string.IsNullOrEmpty(name) || c.npc.name == name))
            {
                return c;
            }

            if (string.IsNullOrEmpty(name))
            {
                return c;
            }

            Character best = null;
            float bestD = 400f * 400f;
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n.name != name)
                {
                    continue;
                }

                var ch = n.GetComponent<Character>();
                if (ch == null || !ch.alive)
                {
                    continue;
                }

                float d = FlatSq(ch.transform.position, new Vector3(x, 0f, z));
                if (d < bestD)
                {
                    bestD = d;
                    best = ch;
                }
            }

            Character chosen = best;
            if (chosen == null)
            {
                chosen = c;
            }

            return chosen;
        }

        /// <summary>
        /// Locking a door, and opening a lock or a padlock other than with a
        /// key, a lockpick or the code, is only done by a scripted event's door
        /// steps (GameEvent modifyDoor and unlockDoor), which are the host's
        /// part (ScriptedEvents.HostSteps) and never run in a client's game for
        /// a place the host has. A player has no way to.
        /// </summary>
        private const string StoryOnly =
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
                return ObjectActions.Allowed(a, obj, f1, x);
            }

            if (a == Action.Explode)
            {
                var ex = ObjRef.Resolve<Explodes>(obj);
                if (ex == null || ex.GetComponent<Character>() != null)
                {
                    Coop.Current.Authority.Refuse(
                        "it isn't here (gone or already blown up)");
                    return false;
                }

                if (BreakSync.Activated(ex))
                {
                    Coop.Current.Authority.Refuse("it had already gone off");
                    return false;
                }

                return Coop.Current.Authority.InReach(
                    ex.transform.position, ShotReach, ex.name);
            }

            if (a >= Action.ItemHit)
            {
                var item = ObjRef.Resolve<Item>(obj);
                if (item == null)
                {
                    Coop.Current.Authority.Refuse("not here");
                    return false;
                }

                float reach;
                if (a == Action.ItemHit)
                {
                    reach = ShotReach;
                }
                else
                {
                    reach = Reach;
                }

                if (!Coop.Current.Authority.InReach(
                    item.transform.position, reach, item.name))
                {
                    return false;
                }

                switch (a)
                {
                    case Action.ItemHit when item.destroyed:
                        Coop.Current.Authority.Refuse("it is already broken");
                        return false;
                    case Action.ItemHit when x > 1000f || x < 0f:
                        Coop.Current.Authority.Refuse(
                            $"a hit of {x:0} is more than any weapon does");
                        return false;
                    case Action.ItemBroken when item.destroyed:
                        Coop.Current.Authority.Refuse("it is already broken");
                        return false;
                    case Action.ItemBroken when !item.destructible:
                        Coop.Current.Authority.Refuse("it can't be broken");
                        return false;
                    case Action.ItemRepaired when !item.destroyed:
                        Coop.Current.Authority.Refuse("it isn't broken");
                        return false;
                }

                return true;
            }

            if (a == Action.NpcDied)
            {
                var npc = DyingNpc(obj, str, x, y);
                if (npc == null)
                {
                    Coop.Current.Authority.Refuse("no such NPC here");
                    return false;
                }

                if (!Coop.Current.Authority.InReach(
                    npc.transform.position, 2500f, npc.name))
                {
                    return false;
                }

                FlagsSync instance = FlagsSync.Instance;
                if (instance != null)
                {
                    string name;
                    if (npc.npc != null)
                    {
                        name = npc.npc.name;
                    }
                    else
                    {
                        name = npc.name;
                    }

                    instance.DeathAllowed(name);
                }

                return true;
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
                var item = ObjRef.Resolve<Item>(obj);
                if (item == null)
                {
                    Coop.Current.Authority.Refuse("not here");
                    return false;
                }

                return Coop.Current.Authority.InReach(
                    item.transform.position, Reach, item.name);
            }

            if (a >= Action.TrapFired)
            {
                return Coop.Current.Traps.Allowed(a, obj);
            }

            if (a < Action.WindowBarricade)
            {
                var door = ObjRef.Resolve<Door>(obj);
                if (door == null)
                {
                    Coop.Current.Authority.Refuse("no such door here");
                    return false;
                }

                // A hit can come from a shot or our flamethrower's flame
                // (BreakSync.OwnFlame), which reach further than a hand.
                float doorReach;
                if (a == Action.DoorHit)
                {
                    doorReach = ShotReach;
                }
                else
                {
                    doorReach = Reach;
                }

                if (!Coop.Current.Authority.InReach(
                    door.transform.position, doorReach, door.name))
                {
                    return false;
                }

                var locked = door.GetComponent<Locked>();
                bool isLocked = locked != null && locked.locked;
                switch (a)
                {
                    case Action.DoorOpen when door.destroyed:
                        Coop.Current.Authority.Refuse("the door is broken");
                        return false;
                    case Action.DoorOpen when isLocked:
                        Coop.Current.Authority.Refuse("the door is locked");
                        return false;
                    case Action.DoorOpen when door.blocked:
                        Coop.Current.Authority.Refuse("the door is blocked");
                        return false;
                    // On a broken door the hammer's strokes rebuild it
                    // (Door.setBarricadeState calls unDestroy): that's a
                    // repair, allowed.
                    case Action.DoorBarricade when door.barricaded
                        && !door.destroyed:
                        Coop.Current.Authority.Refuse(
                            "the door is already barricaded");
                        return false;
                    case Action.DoorDestroyBarricade when !door.barricaded:
                        Coop.Current.Authority.Refuse(
                            "the door isn't barricaded");
                        return false;
                    case Action.DoorHit when door.destroyed:
                        Coop.Current.Authority.Refuse(
                            "the door is already broken");
                        return false;
                    case Action.DoorUnlock when locked == null:
                        Coop.Current.Authority.Refuse(
                            "the door has no lock to open");
                        return false;
                    case Action.DoorUnlock when !locked.locked:
                        Coop.Current.Authority.Refuse("the door isn't locked");
                        return false;
                    case Action.DoorUnlock when x == 0f:
                        Coop.Current.Authority.Refuse(StoryOnly);
                        return false;
                    case Action.DoorLock:
                        Coop.Current.Authority.Refuse(StoryOnly);
                        return false;
                    case Action.DoorUnblock:
                        Coop.Current.Authority.Refuse(StoryOnly);
                        return false;
                    case Action.DoorUnlock when x == 1f && !HasKey(locked):
                        Coop.Current.Authority.Refuse(
                            $"nobody has the key ({locked.keyType})");
                        return false;
                    case Action.DoorHit when x > 1000f:
                        Coop.Current.Authority.Refuse(
                            $"a hit of {x:0} is more than any weapon does");
                        return false;
                }

                return true;
            }

            var window = ObjRef.Resolve<Window>(obj);
            if (window == null)
            {
                Coop.Current.Authority.Refuse("no such window here");
                return false;
            }

            float windowReach;
            if (a == Action.WindowHit)
            {
                windowReach = ShotReach;
            }
            else
            {
                windowReach = Reach;
            }

            if (!Coop.Current.Authority.InReach(
                window.transform.position, windowReach, window.name))
            {
                return false;
            }

            if (a == Action.WindowBarricade && window.barricaded)
            {
                Coop.Current.Authority.Refuse(
                    "the window is already barricaded");
                return false;
            }

            if (a == Action.WindowDestroyBarricade && !window.barricaded)
            {
                Coop.Current.Authority.Refuse("the window isn't barricaded");
                return false;
            }

            if (a == Action.WindowHit && x > 1000f)
            {
                Coop.Current.Authority.Refuse(
                    $"a hit of {x:0} is more than any weapon does");
                return false;
            }

            return true;
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
                var l = door.GetComponent<Locked>();
                w.W.Write((byte)Kind.Door);
                int flags = 0;
                if (door.opened)
                {
                    flags |= 1;
                }

                if (door.barricaded)
                {
                    flags |= 2;
                }

                if (door.destroyed)
                {
                    flags |= 4;
                }

                if (l != null && l.locked)
                {
                    flags |= 8;
                }

                if (door.blocked)
                {
                    flags |= 16;
                }

                w.W.Write((byte)flags);
                w.W.Write(door.health);
                w.W.Write(door.barricadeHealth);
                string s = "";
                if (l != null)
                {
                    s = l.keyType;
                    if (s == null)
                    {
                        s = "";
                    }
                }

                w.WriteStr(s);
            }
            else if (window != null)
            {
                w.W.Write((byte)Kind.Window);
                int barricadedFlag = 0;
                if (window.barricaded)
                {
                    barricadedFlag = 1;
                }

                w.W.Write((byte)barricadedFlag);
                w.W.Write(window.barricadeHealth);
            }
            else if (trap != null)
            {
                w.W.Write((byte)Kind.Trap);
                Coop.Current.Traps.WriteState(w, trap);
            }
            else if (item != null)
            {
                var l = item.GetComponent<Locked>();
                w.W.Write((byte)Kind.Item);
                var ii = item.GetComponent<InteractiveItem>();
                var feeder = item.GetComponent<Feeder>();
                int itemFlags = 0;
                if (item.isOn)
                {
                    itemFlags |= 1;
                }

                if (l != null && l.locked)
                {
                    itemFlags |= 2;
                }

                if (item.searched)
                {
                    itemFlags |= 4;
                }

                if (item.destroyed)
                {
                    itemFlags |= 8;
                }

                if (ii != null && ii.isOn)
                {
                    itemFlags |= 16;
                }

                if (feeder != null && feeder.Active)
                {
                    itemFlags |= 32;
                }

                w.W.Write((byte)itemFlags);
                w.W.Write(item.health);
            }
            else if (go != null && go.GetComponent<Explodes>() != null)
            {
                w.W.Write((byte)Kind.Explosive);
                int activated = 0;
                if (BreakSync.Activated(go.GetComponent<Explodes>()))
                {
                    activated = 1;
                }

                w.W.Write((byte)activated);
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
            var leftWas = ScriptedEvents.LeftToActor;
            ScriptedEvents.LeftToActor = ScriptedEvents.ActionTypes;
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
                            byte f = r.R.ReadByte();
                            int health = r.R.ReadInt32(),
                                barricade = r.R.ReadInt32();
                            string key = r.ReadStr();
                            Door door = null;
                            if (go != null)
                            {
                                door = go.GetComponent<Door>();
                            }

                            if (door == null)
                            {
                                break;
                            }

                            bool open = (f & 1) != 0,
                                barricaded = (f & 2) != 0,
                                broken = (f & 4) != 0,
                                locked = (f & 8) != 0;
                            // only the story sets or clears it (Door.unblock),
                            // and only the host runs that
                            door.blocked = (f & 16) != 0;
                            if (broken && !door.destroyed)
                            {
                                // silently: a rollback, not the door breaking
                                // again
                                door.destroyDoor(true);
                            }
                            else if (!broken && door.destroyed)
                            {
                                door.unDestroy();
                            }

                            var l = door.GetComponent<Locked>();
                            if (locked && (l == null || !l.locked))
                            {
                                door.lockMe(key);
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
                                    door.transform.position
                                        + door.transform.right,
                                    null, 0f);
                            }
                            else if (!open && door.opened)
                            {
                                door.close(null);
                            }

                            door.health = health;
                            door.barricadeHealth = barricade;
                            break;
                        }

                    case Kind.Window:
                        {
                            bool barricaded = (r.R.ReadByte() & 1) != 0;
                            int barricade = r.R.ReadInt32();
                            Window window = null;
                            if (go != null)
                            {
                                window = go.GetComponent<Window>();
                            }

                            if (window == null)
                            {
                                break;
                            }

                            if (barricaded && !window.barricaded)
                            {
                                window.barricadeState = 2;
                                window.barricade(barricade, false);
                            }
                            else if (!barricaded && window.barricaded)
                            {
                                window.destroyBarricade(false);
                            }

                            window.barricadeHealth = barricade;
                            break;
                        }

                    case Kind.Trap:
                        Coop.Current.Traps.ReadState(r, go);
                        break;

                    case Kind.Item:
                        {
                            byte f = r.R.ReadByte();
                            int health = r.R.ReadInt32();
                            bool on = (f & 1) != 0, broken = (f & 8) != 0;
                            Item item = null;
                            if (go != null)
                            {
                                item = go.GetComponent<Item>();
                            }

                            if (item == null)
                            {
                                break;
                            }

                            if (item.isOn != on)
                            {
                                if (on)
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
                                l.locked = (f & 2) != 0;
                            }

                            item.searched = (f & 4) != 0;
                            var ii = item.GetComponent<InteractiveItem>();
                            if (ii != null)
                            {
                                ii.isOn = (f & 16) != 0;
                            }

                            var feeder = item.GetComponent<Feeder>();
                            if (feeder != null
                                && feeder.Active != ((f & 32) != 0))
                            {
                                if ((f & 32) != 0)
                                {
                                    feeder.makeActive();
                                }
                                else
                                {
                                    feeder.makeInactive();
                                }
                            }

                            if (broken && !item.destroyed && item.destructible)
                            {
                                item.die();
                            }
                            else if (!broken && item.destroyed)
                            {
                                item.setNormalState();
                            }

                            item.health = health;
                            break;
                        }

                    case Kind.Explosive:
                        {
                            // an explosion here can't be taken back; the next
                            // state the host sends is how it is
                            r.R.ReadByte();
                            break;
                        }

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
                ScriptedEvents.LeftToActor = leftWas;
            }
        }

        // ---- what changed ---------------------------------------------------

        public struct DoorState
        {
            public bool Opened, Barricaded, Destroyed;
            public int Health, BarricadeHealth;
            public static DoorState Of(Door d) => new DoorState
            {
                Opened = d.opened,
                Barricaded = d.barricaded,
                Destroyed = d.destroyed,
                Health = d.health,
                BarricadeHealth = d.barricadeHealth
            };

            public bool Same(DoorState o) => Opened == o.Opened
                && Barricaded == o.Barricaded && Destroyed == o.Destroyed
                && Health == o.Health && BarricadeHealth == o.BarricadeHealth;
        }

        public struct WindowState
        {
            public bool Barricaded;
            public int BarricadeHealth;
            public static WindowState Of(Window w) => new WindowState
            {
                Barricaded = w.barricaded,
                BarricadeHealth = w.barricadeHealth
            };

            public bool Same(WindowState o) => Barricaded == o.Barricaded
                && BarricadeHealth == o.BarricadeHealth;
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

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorOpenPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(
            Door __instance, Vector3 openerPosition, Transform openerTransform,
            WorldActions.DoorState __state)
        {
            if (!Coop.Guards.Outermost() || !Coop.Guards.Sharing
                || __state.Same(WorldActions.DoorState.Of(__instance)))
            {
                return;
            }

            Vector3 at;
            if (openerTransform != null)
            {
                at = openerTransform.position;
            }
            else
            {
                at = openerPosition;
            }

            WorldActions.Send(
                WorldActions.Action.DoorOpen, __instance.gameObject, at.x,
                at.z);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.close))]
    static class DoorClosePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(Door __instance, WorldActions.DoorState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.DoorState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.DoorClose, __instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.barricade))]
    static class DoorBarricadePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(
            Door __instance, bool byPlayer, WorldActions.DoorState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.DoorState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.DoorBarricade, __instance.gameObject,
                    f1: byPlayer);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyBarricade))]
    static class DoorDestroyBarricadePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(
            Door __instance, bool silent, WorldActions.DoorState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.DoorState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.DoorDestroyBarricade,
                    __instance.gameObject, f1: silent);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyDoor))]
    static class DoorDestroyPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(
            Door __instance, bool silently, WorldActions.DoorState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.DoorState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.DoorDestroy, __instance.gameObject,
                    f1: silently);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unDestroy))]
    static class DoorUndestroyPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
        }

        static void Postfix(Door __instance, WorldActions.DoorState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.DoorState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.DoorUndestroy, __instance.gameObject);
            }
        }
    }

    /// <summary>
    /// Damage to a door or its barricade. Only the hit is sent (not what it
    /// causes): the other games make the same call, which takes off the same
    /// health and breaks the barricade or door when it runs out, as it does
    /// here.
    /// </summary>
    [HarmonyPatch(
        typeof(Door), nameof(Door.getHit),
        new[] { typeof(int), typeof(Transform), typeof(bool), typeof(bool) })]
    static class DoorHitPatch
    {
        /// <summary>
        /// On a client, a hit the host makes too (an explosion, fire) isn't
        /// made here: the host's DoorHit is.
        /// </summary>
        static bool Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
            return !BreakSync.HostsHit();
        }

        static void Postfix(
            Door __instance, int damage, bool normalHit, bool canDamageMetal,
            WorldActions.DoorState __state)
        {
            if (!Coop.Guards.Outermost() || !Coop.Guards.Sharing
                || __state.Same(WorldActions.DoorState.Of(__instance)))
            {
                return;
            }

            WorldActions.Send(
                WorldActions.Action.DoorHit, __instance.gameObject, damage,
                f1: normalHit, f2: canDamageMetal,
                byPlayerKnown: BreakSync.OwnFlame());
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.lockMe))]
    static class DoorLockPatch
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Door __instance, string keyType)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing)
            {
                WorldActions.Send(
                    WorldActions.Action.DoorLock, __instance.gameObject,
                    str: keyType);
            }
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unlock))]
    static class DoorUnlockPatch
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Door __instance)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing)
            {
                WorldActions.Send(
                    WorldActions.Action.DoorUnlock, __instance.gameObject);
            }
        }
    }

    /// <summary>
    /// The story clearing a door's "blocked" (GameEvent modifyDoor unblock),
    /// which a player can't open through until then (Player.itemDefaultAction
    /// says "blocked"). That step is the host's (ScriptedEvents.HostSteps), so
    /// the host sends it to everyone; without this the joiners' copies stayed
    /// blocked for good (Piotrek's door). Door.unblock only sets a field, so
    /// Mono inlines it into its caller and a patch on it never runs (tried: the
    /// host unblocked, nothing was sent). The step's coroutine (GameEvent.fire,
    /// the only caller) calls DoorUnblockPatch.Unblock instead, by a
    /// transpiler.
    /// </summary>
    [HarmonyPatch]
    static class DoorUnblockPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in typeof(GameEvent).GetNestedTypes(
                BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (t.Name.StartsWith("<fire>")
                    && typeof(System.Collections.IEnumerator).IsAssignableFrom(
                        t))
                {
                    var m = AccessTools.Method(t, "MoveNext");
                    if (m != null)
                    {
                        yield return m;
                    }
                }
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var unblock =
                AccessTools.Method(typeof(Door), nameof(Door.unblock));
            var ours =
                AccessTools.Method(typeof(DoorUnblockPatch), nameof(Unblock));
            int swapped = 0;
            foreach (var ci in instructions)
            {
                if (ci.Calls(unblock))
                {
                    yield return new CodeInstruction(OpCodes.Call, ours)
                    {
                        labels = ci.labels,
                        blocks = ci.blocks
                    };

                    swapped++;
                }
                else
                {
                    yield return ci;
                }
            }

            if (swapped == 0)
            {
                Plugin.Log.LogWarning(
                    "DoorUnblockPatch: no Door.unblock in GameEvent.fire (game "
                        + "changed?)");
            }
        }

        /// <summary>
        /// Door.unblock, and sent to the others when it cleared the block here.
        /// </summary>
        public static void Unblock(Door door)
        {
            bool was = door.blocked;
            door.unblock();
            if (was && !door.blocked && Coop.Guards.Depth == 0
                && Coop.Guards.Sharing)
            {
                WorldActions.Send(
                    WorldActions.Action.DoorUnblock, door.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.barricade))]
    static class WindowBarricadePatch
    {
        static void Prefix(
            Window __instance, out WorldActions.WindowState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.WindowState.Of(__instance);
        }

        static void Postfix(
            Window __instance, int destHealth, bool byPlayer,
            WorldActions.WindowState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.WindowState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.WindowBarricade, __instance.gameObject,
                    destHealth, f1: byPlayer);
            }
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.destroyBarricade))]
    static class WindowDestroyBarricadePatch
    {
        static void Prefix(
            Window __instance, out WorldActions.WindowState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.WindowState.Of(__instance);
        }

        static void Postfix(
            Window __instance, bool silent, WorldActions.WindowState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.WindowState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.WindowDestroyBarricade,
                    __instance.gameObject, f1: silent);
            }
        }
    }

    [HarmonyPatch(
        typeof(Window), nameof(Window.getHit),
        new[] { typeof(int), typeof(Transform), typeof(bool) })]
    static class WindowHitPatch
    {
        /// <summary>
        /// On a client, a hit the host makes too (an explosion, fire) isn't
        /// made here: the host's WindowHit is.
        /// </summary>
        static bool Prefix(
            Window __instance, out WorldActions.WindowState __state)
        {
            Coop.Guards.Depth++;
            __state = WorldActions.WindowState.Of(__instance);
            return !BreakSync.HostsHit();
        }

        static void Postfix(
            Window __instance, int damage, bool normalHit,
            WorldActions.WindowState __state)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing
                && !__state.Same(WorldActions.WindowState.Of(__instance)))
            {
                WorldActions.Send(
                    WorldActions.Action.WindowHit, __instance.gameObject,
                    damage, f1: normalHit, byPlayerKnown: BreakSync.OwnFlame());
            }
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// Lamps, generators, radios...: switched on or off for everyone.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class ItemOnPatch
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Item __instance)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing)
            {
                WorldActions.Send(
                    WorldActions.Action.ItemOn, __instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.turnOff))]
    static class ItemOffPatch
    {
        static void Prefix() => Coop.Guards.Depth++;
        static void Postfix(Item __instance)
        {
            if (Coop.Guards.Outermost() && Coop.Guards.Sharing)
            {
                WorldActions.Send(
                    WorldActions.Action.ItemOff, __instance.gameObject);
            }
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// An NPC (a trader...) died. NPCs live in each player's game; whoever's
    /// game it died in tells the others, and it dies there too. (The game marks
    /// some deaths only with a story flag, which FlagsSync shares as well.)
    /// A client's NPCs can only be killed by its own player or the story, so
    /// its NPC deaths are always news.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.die))]
    static class NpcDiedPatch
    {
        static void Prefix(Character __instance, out bool __state)
        {
            Coop.Guards.Depth++;
            __state = __instance.alive;
        }

        static void Postfix(Character __instance, bool __state)
        {
            if (__state && __instance != null && __instance.npc != null
                && !__instance.alive && Net.Session.Instance != null)
            {
                Combat.HostileNpcs.Died(__instance); // a replayed death too
            }

            if (!Coop.Guards.Outermost() || !Coop.Guards.Sharing
                || __instance == null || __instance.npc == null || !__state)
            {
                return;
            }

            // With its name and where it stood: one each game puts in place
            // itself (the morning trader, the wolf) has its own save id in
            // each, and one that walked can be off its named spot.
            var p = __instance.transform.position;
            WorldActions.Send(
                WorldActions.Action.NpcDied, __instance.gameObject, x: p.x,
                y: p.z, str: __instance.npc.name, byPlayerKnown: true);
        }
    }

    /// <summary>
    /// Killing the night trader sends the killer into a dream
    /// (`Character.die`'s isNightTrader branch calls
    /// `Player.special_onKillNightTrader`). When the death is replayed from
    /// another game, that dream belongs to the killer, not to us: skip it. The
    /// branch's flag (nightTrader_killedYesterday) and `endAfterNight` still
    /// run.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.special_onKillNightTrader))]
    static class NightTraderDreamPatch
    {
        static bool Prefix()
        {
            if (!Coop.Guards.Replaying)
            {
                return true;
            }

            Plugin.Log.LogInfo(
                "[world] the night trader was killed in another game: the "
                    + "killer's dream isn't ours");
            return false;
        }
    }
}
