using System;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Doors and windows, shared: when one is opened, closed, barricaded, damaged, broken, locked or unlocked in a
    /// player's game, the same call is made in everyone else's, so it looks and sounds as it does for them.
    ///
    /// WorldAction: u8 action, ObjRef, f32 a, f32 b, u8 flags, str s.
    /// A client's go to the host, which makes them and passes them on; the host's go to everyone. Only calls that
    /// changed something are sent (opening an open door, or trying a padlocked one, isn't).
    /// </summary>
    public static class WorldActions
    {
        public enum Action : byte
        {
            DoorOpen = 1, DoorClose = 2, DoorBarricade = 3, DoorDestroyBarricade = 4, DoorDestroy = 5, DoorHit = 6,
            /// <summary>DoorLock: s the key type. DoorUnlock: a is 0 by the story, 1 with a key, 2 with a lockpick (Locked.unlock).</summary>
            DoorLock = 7, DoorUnlock = 8, DoorUndestroy = 9,
            WindowBarricade = 11, WindowDestroyBarricade = 12, WindowHit = 13,
            TrapFired = 21, TrapSwitched = 22,
            /// <summary>Client to host: our player finished disarming this trap, or picking this locked crate; the host rolls whether it worked (Verdict result "1" or "0").</summary>
            TrapDisarm = 23,
            ItemOn = 31, ItemOff = 32,
            /// <summary>A lock on something that isn't a door (a chest, a cabinet) was opened. a: 0 by the story, 1 with a key, 2 with a lockpick.</summary>
            ItemUnlock = 33,
            /// <summary>Something with an inventory is searched now (Item.searched, and Character.searched on a corpse), which saves and shows "(searched)" on hover. Host to clients: a locked crate (Item.disarmTime &gt; 0) was picked open, so it opens without the progress bar, or the host's player opened a container. Client to host: our player opened a container (Item.openInventory); never a crate that isn't picked open yet.</summary>
            ItemSearched = 34,
            /// <summary>A combination padlock was opened. flag 1: by a player who entered the code (s: the code they entered).</summary>
            PadlockUnlock = 35,
            NpcDied = 41,
            /// <summary>Furniture or another destructible item took a hit: a = damage, b = its health after it, f1 = normalHit.</summary>
            ItemHit = 51,
            /// <summary>A destructible item broke without a hit (Item.die called by itself).</summary>
            ItemBroken = 52,
            /// <summary>A broken item was repaired (Item.setNormalState, from building).</summary>
            ItemRepaired = 53,
            /// <summary>An exploding barrel or gas bottle went off (Explodes.onActivate): f1 = flaming.</summary>
            Explode = 61,
            /// <summary>An InteractiveItem (a switch shown in its own window) was switched. flag 1: it's on now (World.ObjectActions).</summary>
            InteractiveSwitch = 90,
            /// <summary>Something was taken off the ground or eaten: a note picked up, a death bag's experience taken, a lure the host's animals ate. The others remove their copy (World.ObjectActions).</summary>
            Removed = 91,
            /// <summary>A feeder was used: it's empty until the next dawn, for everyone (World.ObjectActions). A client's waits for the host's yes.</summary>
            FeederUsed = 92,
            /// <summary>Spilt gasoline (a Liquid: a trail or a puddle) caught fire. Host to clients: where the fire started (it spreads by itself in each game). Client to host: our player's burning weapon lit it (World.ObjectActions).</summary>
            LiquidLit = 93,
            /// <summary>A vine (ChainParent) was cut at: x = damage, y = its health after the hit (World.ObjectActions). Detaching at 0 follows in each game.</summary>
            VineHit = 130,
            /// <summary>Host to clients: the shadow covering an item (ShadowArmor) died, in light or from hits (World.ObjectActions).</summary>
            ShadowArmorDied = 131,
        }

        private static readonly System.Reflection.MethodInfo OnAfterTrigger = AccessTools.Method(typeof(Trigger), "OnAfterTrigger");

        /// <summary>True while making a call that came from another game, so it isn't sent back.</summary>
        public static bool Replaying;
        /// <summary>"--dwmp-debug-world": log every shared-world message sent and applied.</summary>
        public static bool Debug;

        /// <summary>
        /// How deep in patched calls we are. Only the outermost is sent: a hit that breaks a barricade calls
        /// destroyBarricade itself, and the other games' hit will do the same. Reset every frame, in case a call threw.
        /// </summary>
        public static int Depth;
        public static void ResetDepth() => Depth = 0;
        public static bool Outermost() => --Depth <= 0 && (Depth = 0) == 0;

        private static Session S => Session.Instance;

        /// <summary>
        /// Whether this game shares its world right now (the host with players in, or a client in it). Never while
        /// a world loads: the save loader makes the saved dropped items, thrown things and the rest again, and a
        /// joiner sending those as new would make the host a second copy of each (WorldShare.Loading).
        /// </summary>
        public static bool Sharing =>
            S != null && !Replaying && WorldShare.InWorld && !WorldShare.Loading && ((S.IsHost && S.Peers.Count > 0) || NetEnemies.ClientMode);

        public static void Register(Session s)
        {
            s.Register(MsgType.WorldAction, OnAction);
            s.Register(MsgType.ObjState, OnObjState);
            s.OnSessionEnded += TrapFiredPatch.Reset;
        }

        /// <summary>How far from a thing a player may be to act on it (generous: positions arrive a little late).</summary>
        public const float Reach = 600f;
        /// <summary>How far a player may be from what they shot (the game's shots reach 1000 px), with the same margin.</summary>
        public const float ShotReach = 1200f;

        /// <summary>
        /// What a client sends is what its player did. The world also changes by itself (scripts that barricade a
        /// house at dusk, enemies breaking in): that's the host's to say, and its game does it there too.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<Type> PlayerSide = new System.Collections.Generic.HashSet<Type>
        {
            typeof(Player), typeof(InputScript), typeof(InvSlot), typeof(InventoryController), typeof(ConstructionMenu), typeof(Padlock), typeof(global::Button),
        };

        /// <summary>Tests only: count what's done now as our own player's doing.</summary>
        public static bool TestAsPlayer;

        public static bool ByLocalPlayer()
        {
            if (TestAsPlayer) return true;
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 24; i++)
            {
                var t = st.GetFrame(i)?.GetMethod()?.DeclaringType;
                for (; t != null; t = t.DeclaringType) if (PlayerSide.Contains(t)) return true; // lambdas/iterators are nested types
            }
            return false;
        }

        public static void Send(Action a, GameObject go, float x = 0, float y = 0, bool f1 = false, bool f2 = false, string str = "", bool byPlayerKnown = false)
        {
            if (S.IsClient && !byPlayerKnown && !ByLocalPlayer()) return;
            Send(a, ObjRef.Of(go), go.name, x, y, f1, f2, str);
        }

        /// <summary>
        /// Send an action on a thing named before the call (it may be gone by now, like a barrel that blew up).
        /// The caller has checked that it's ours to send.
        /// </summary>
        public static void Send(Action a, ObjRef r, string name, float x = 0, float y = 0, bool f1 = false, bool f2 = false, string str = "")
        {
            if (!r.Valid) return;
            var w = S.Begin(MsgType.WorldAction);
            WriteAction(w, a, r, x, y, f1, f2, str);
            if (Debug) Plugin.Log.LogInfo($"[world] sent {a} {r} ({name})");
            if (S.IsHost) S.Broadcast(w, Channel.Control, reliable: true);
            else Authority.Request(w, $"{a} {r} ({name})");
        }

        private static void WriteAction(PacketWriter w, Action a, ObjRef r, float x, float y, bool f1, bool f2, string str)
        {
            w.W.Write((byte)a);
            r.Write(w);
            w.W.Write(x); w.W.Write(y);
            w.W.Write((byte)((f1 ? 1 : 0) | (f2 ? 2 : 0)));
            w.WriteStr(str ?? "");
        }

        private static void OnAction(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            var a = (Action)r.R.ReadByte();
            var obj = ObjRef.Read(r);
            float x = r.R.ReadSingle(), y = r.R.ReadSingle();
            byte f = r.R.ReadByte();
            string str = r.ReadStr();
            bool f1 = (f & 1) != 0, f2 = (f & 2) != 0;

            if (s.IsHost && Authority.Handling && !Allowed(a, obj, x, y, f1, str))
            {
                SendState(from, obj, padlock: a == Action.PadlockUnlock);
                return;
            }

            // Applied as outside any explosion or flame, whatever this frame was in when the message was read: on a
            // client a hit inside one is left to the host (BreakSync.HostsHit), and this is the host's hit.
            int thrownDepth = ThrownHits.Depth; var flame = ThrownHits.CurrentFlame;
            ThrownHits.Depth = 0; ThrownHits.CurrentFlame = null;
            Replaying = true;
            try
            {
                if (a == Action.TrapDisarm && Crate(ObjRef.Resolve<Item>(obj)) is Item crate)
                {
                    // A locked crate: the host rolls the pick, as the game would for its own player. If it worked
                    // the crate is searched here and the others are told (ItemSearched); a failed pick changes nothing.
                    bool was = crate.searched;
                    bool worked = was || crate.disarmChance > UnityEngine.Random.Range(0f, 1f);
                    Authority.Result(worked ? "1" : "0");
                    if (Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(from)} picks the lock of {obj} ({crate.name}): {(was ? "already open" : worked ? "worked" : "failed")}");
                    if (!worked || was) return;
                    crate.searched = true;
                    a = Action.ItemSearched;
                }
                else if (a == Action.TrapDisarm)
                {
                    // Only ever a request to the host: roll it here, as the game rolls it for its own player.
                    var item = ObjRef.Resolve<Item>(obj);
                    bool worked = item != null && item.disarmChance > UnityEngine.Random.Range(0f, 1f);
                    Authority.Result(worked ? "1" : "0");
                    if (Debug) Plugin.Log.LogInfo($"[world] {s.NameOf(from)} disarms {obj}: {(worked ? "worked" : "failed")}");
                    if (item != null) item.switchTriggerState(); // gone (or set off) here either way; the trap goes for the others as TrapSwitched
                    a = Action.TrapSwitched;
                }
                else if (ObjectActions.Handles(a))
                {
                    ObjectActions.Apply(a, obj, f1, x, ref y);
                }
                else if (a == Action.Explode)
                {
                    var ex = ObjRef.Resolve<Explodes>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (ex != null ? ex.name : "not found here"));
                    if (ex == null || ex.GetComponent<Character>() != null) return; // never an enemy's (see BreakSync)
                    if (!BreakSync.Activated(ex)) ex.onActivate(f1);
                }
                else if (a >= Action.ItemHit)
                {
                    var item = ObjRef.Resolve<Item>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (item != null ? item.name : "not found here"));
                    if (item == null) return;
                    switch (a)
                    {
                        case Action.ItemHit:
                            if (item.destroyed) break;
                            // A client's hit is applied to the host's health; the host's own says what health it left,
                            // so a copy that missed a hit (or took one more) comes back in step.
                            if (!s.IsHost && item.shadowArmor == null) item.health = (int)y + (int)x; // a shadow takes the hit, not the item
                            item.getHit((int)x, null, f1);
                            if (s.IsHost) y = item != null ? item.health : 0;
                            break;
                        case Action.ItemBroken: if (!item.destroyed) item.die(); break;
                        case Action.ItemRepaired: if (item.destroyed) item.setNormalState(); break;
                    }
                }
                else if (a == Action.NpcDied)
                {
                    var npc = DyingNpc(obj, str, x, y);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (npc != null ? npc.name : "not found here"));
                    // A copy of an NPC the host runs as an enemy dies with the host's state (Combat.NetEnemies).
                    bool puppet = npc != null && Combat.NetEnemies.Instance != null && Combat.NetEnemies.Instance.IsPuppet(npc, out _);
                    if (npc != null && npc.alive && !puppet) npc.die();
                }
                else if (a == Action.ItemUnlock)
                {
                    var l = ObjRef.Resolve<Locked>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (l != null ? l.name : "not found here"));
                    if (l != null) l.unlock();
                }
                else if (a == Action.ItemSearched)
                {
                    var item = ObjRef.Resolve<Item>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (item != null ? item.name : "not found here"));
                    if (item == null) return;
                    item.searched = true;
                    var corpse = item.GetComponent<Character>();
                    if (corpse != null) corpse.searched = true;
                }
                else if (a == Action.PadlockUnlock)
                {
                    var padlock = ObjRef.Resolve<Padlock>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (padlock != null ? padlock.name : "not found here"));
                    if (padlock == null) return;
                    padlock.unlock(false); // without its window, message and events: those are the opener's
                }
                else if (a >= Action.ItemOn)
                {
                    var item = ObjRef.Resolve<Item>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (item != null ? item.name : "not found here"));
                    if (item == null) return;
                    if (a == Action.ItemOn) item.turnOn(); else item.turnOff();
                }
                else if (a >= Action.TrapFired)
                {
                    var trap = ObjRef.Resolve<Trigger>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (trap != null ? trap.name : "not found here"));
                    // Our own firing of it, not answered yet, left its spawn for this (TrapFiredPatch).
                    if (a == Action.TrapFired && s.IsClient && TrapFiredPatch.SpawnHeld(obj)) return;
                    if (trap == null) return;
                    if (a == Action.TrapFired) { if (trap.active) OnAfterTrigger.Invoke(trap, new object[] { null, false }); }
                    else trap.GetComponent<Item>()?.switchTriggerState();
                }
                else if (a < Action.WindowBarricade)
                {
                    var door = ObjRef.Resolve<Door>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (door != null ? door.name : "not found here"));
                    if (door == null) return;
                    switch (a)
                    {
                        case Action.DoorOpen: door.open(new Vector3(x, door.transform.position.y, y), null, 0f); break;
                        case Action.DoorClose: door.close(null); break;
                        // As the hammer's third stroke: Door.barricade only nails it up when barricadeState reaches 3.
                        case Action.DoorBarricade: door.barricadeState = 2; door.barricade(f1); break;
                        case Action.DoorDestroyBarricade: door.destroyBarricade(f1); break;
                        case Action.DoorDestroy: door.destroyDoor(f1); break;
                        case Action.DoorHit: door.getHit((int)x, null, f1, f2); break;
                        case Action.DoorLock: door.lockMe(str); break;
                        case Action.DoorUnlock: door.unlock(); break;
                        case Action.DoorUndestroy: door.unDestroy(); break;
                    }
                }
                else
                {
                    var window = ObjRef.Resolve<Window>(obj);
                    if (Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (window != null ? window.name : "not found here"));
                    if (window == null) return;
                    switch (a)
                    {
                        case Action.WindowBarricade: window.barricadeState = 2; window.barricade((int)x, f1); break; // as for a door
                        case Action.WindowDestroyBarricade: window.destroyBarricade(f1); break;
                        case Action.WindowHit: window.getHit((int)x, null, f1); break;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't apply {a} to {obj}: {e.Message}"); }
            finally { Replaying = false; ThrownHits.Depth = thrownDepth; ThrownHits.CurrentFlame = flame; }

            // A padlock a client's player opened: its events (onTryToOpenLocked, onUnlockPadlock) ran in the opener's
            // game, which sends them as scripted events of its own (ScriptedEvents): the host runs its part of them then.

            if (s.IsHost) ObjectActions.AfterOnHost(a, obj, f1);

            // The host passes a client's action on to the others.
            if (s.IsHost)
            {
                var w = s.Begin(MsgType.WorldAction);
                WriteAction(w, a, obj, x, y, f1, f2, str);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        // ---- the host's checks -------------------------------------------------

        /// <summary>
        /// Host: whether a client's player could have done this, as this world is now: the thing is here, they're
        /// near it, and it's in a state the action applies to. Refuses the request if not.
        /// </summary>
        /// <summary>
        /// The NPC an NpcDied names: by its ref, else (an NPC each game placed itself, or one that walked off its
        /// named spot) the living NPC of that name nearest to where it died in the killer's game, within 400.
        /// </summary>
        private static Character DyingNpc(ObjRef obj, string name, float x, float z)
        {
            var c = ObjRef.Resolve<Character>(obj);
            if (c != null && c.npc != null && (string.IsNullOrEmpty(name) || c.npc.name == name)) return c;
            if (string.IsNullOrEmpty(name)) return c;
            Character best = null; float bestD = 400f * 400f;
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n.name != name) continue;
                var ch = n.GetComponent<Character>();
                if (ch == null || !ch.alive) continue;
                var p = ch.transform.position;
                float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d < bestD) { bestD = d; best = ch; }
            }
            return best ?? c;
        }

        private static bool Allowed(Action a, ObjRef obj, float x, float y, bool f1, string str)
        {
            if (ObjectActions.Handles(a)) return ObjectActions.Allowed(a, obj, f1, x);
            if (a == Action.Explode)
            {
                var ex = ObjRef.Resolve<Explodes>(obj);
                if (ex == null || ex.GetComponent<Character>() != null) { Authority.Refuse("it isn't here (gone or already blown up)"); return false; }
                if (BreakSync.Activated(ex)) { Authority.Refuse("it had already gone off"); return false; }
                return Authority.InReach(ex.transform.position, ShotReach, ex.name);
            }
            if (a >= Action.ItemHit)
            {
                var item = ObjRef.Resolve<Item>(obj);
                if (item == null) { Authority.Refuse("not here"); return false; }
                if (!Authority.InReach(item.transform.position, a == Action.ItemHit ? ShotReach : Reach, item.name)) return false;
                switch (a)
                {
                    case Action.ItemHit when item.destroyed: Authority.Refuse("it is already broken"); return false;
                    case Action.ItemHit when x > 1000f || x < 0f: Authority.Refuse($"a hit of {x:0} is more than any weapon does"); return false;
                    case Action.ItemBroken when item.destroyed: Authority.Refuse("it is already broken"); return false;
                    case Action.ItemBroken when !item.destructible: Authority.Refuse("it can't be broken"); return false;
                    case Action.ItemRepaired when !item.destroyed: Authority.Refuse("it isn't broken"); return false;
                }
                return true;
            }
            if (a == Action.NpcDied)
            {
                var npc = DyingNpc(obj, str, x, y);
                if (npc == null) { Authority.Refuse("no such NPC here"); return false; }
                if (!Authority.InReach(npc.transform.position, 2500f, npc.name)) return false;
                FlagsSync.Instance?.DeathAllowed(npc.npc != null ? npc.npc.name : npc.name);
                return true;
            }
            if (a == Action.ItemUnlock)
            {
                var l = ObjRef.Resolve<Locked>(obj);
                if (l == null) { Authority.Refuse("there's no lock on it here"); return false; }
                if (!Authority.InReach(l.transform.position, Reach, l.name)) return false;
                if (x == 1f && !HasKey(l)) { Authority.Refuse($"nobody has the key ({l.keyType})"); return false; }
                return true;
            }
            if (a == Action.ItemSearched)
            {
                // A client's player opened a container. Only the host says a crate was picked open (its TrapDisarm roll).
                var item = ObjRef.Resolve<Item>(obj);
                if (item == null) { Authority.Refuse("no such thing here"); return false; }
                if (Crate(item) != null && !item.searched) { Authority.Refuse("a crate is picked open by the host's roll"); return false; }
                if (item.GetComponent<Inventory>() == null) { Authority.Refuse("there's nothing to search in it"); return false; }
                return Authority.InReach(item.transform.position, Reach, item.name);
            }
            if (a == Action.PadlockUnlock)
            {
                var p = ObjRef.Resolve<Padlock>(obj);
                if (p == null) { Authority.Refuse("no such padlock here"); return false; }
                if (!Authority.InReach(p.transform.position, Reach, p.name)) return false;
                if (f1 && p.locked && str != p.combination) { Authority.Refuse("that isn't the padlock's code"); return false; }
                return true;
            }
            if (a == Action.TrapDisarm && ObjRef.Resolve<Item>(obj) is Item picked && picked.GetComponent<Trigger>() == null)
            {
                if (Crate(picked) == null) { Authority.Refuse("there's no lock to pick on it"); return false; }
                return Authority.InReach(picked.transform.position, Reach, picked.name);
            }
            if (a >= Action.ItemOn)
            {
                var item = ObjRef.Resolve<Item>(obj);
                if (item == null) { Authority.Refuse("not here"); return false; }
                return Authority.InReach(item.transform.position, Reach, item.name);
            }
            if (a >= Action.TrapFired)
            {
                var trap = ObjRef.Resolve<Trigger>(obj);
                if (trap == null) { Authority.Refuse("the trap is gone"); return false; }
                if (!Authority.InReach(trap.transform.position, Reach, trap.name)) return false;
                if (a == Action.TrapFired && trap.active && TrapFiredPatch.TestRefuse != null)
                {
                    // Tests ("trap-refuse"): what makes a real refusal, made here just before this request.
                    if (TrapFiredPatch.TestRefuse == "race") OnAfterTrigger.Invoke(trap, new object[] { null, false }); // it went off here first, and that went to everyone
                    else { trap.active = false; UnityEngine.Object.DestroyImmediate(trap.gameObject); } // it isn't here
                    Authority.Refuse(TrapFiredPatch.TestRefuse == "race" ? "the trap had already gone off (test)" : "the trap is gone (test)");
                    return false;
                }
                if (a == Action.TrapFired && !trap.active) { Authority.Refuse("the trap had already gone off"); return false; }
                if (a == Action.TrapDisarm && (!trap.active || !trap.canDisarm)) { Authority.Refuse("the trap can't be disarmed now"); return false; }
                return true;
            }
            if (a < Action.WindowBarricade)
            {
                var door = ObjRef.Resolve<Door>(obj);
                if (door == null) { Authority.Refuse("no such door here"); return false; }
                // A hit can come from a shot or our flamethrower's flame (BreakSync.OwnFlame), which reach further than a hand.
                if (!Authority.InReach(door.transform.position, a == Action.DoorHit ? ShotReach : Reach, door.name)) return false;
                var locked = door.GetComponent<Locked>();
                bool isLocked = locked != null && locked.locked;
                switch (a)
                {
                    case Action.DoorOpen when door.destroyed: Authority.Refuse("the door is broken"); return false;
                    case Action.DoorOpen when isLocked: Authority.Refuse("the door is locked"); return false;
                    case Action.DoorBarricade when door.barricaded: Authority.Refuse("the door is already barricaded"); return false;
                    case Action.DoorBarricade when door.destroyed: Authority.Refuse("the door is broken"); return false;
                    case Action.DoorDestroyBarricade when !door.barricaded: Authority.Refuse("the door isn't barricaded"); return false;
                    case Action.DoorHit when door.destroyed: Authority.Refuse("the door is already broken"); return false;
                    case Action.DoorUnlock when x > 0f && locked == null: Authority.Refuse("the door has no lock to open"); return false;
                    case Action.DoorUnlock when x == 1f && !HasKey(locked): Authority.Refuse($"nobody has the key ({locked.keyType})"); return false;
                    case Action.DoorHit when x > 1000f: Authority.Refuse($"a hit of {x:0} is more than any weapon does"); return false;
                }
                return true;
            }
            var window = ObjRef.Resolve<Window>(obj);
            if (window == null) { Authority.Refuse("no such window here"); return false; }
            if (!Authority.InReach(window.transform.position, a == Action.WindowHit ? ShotReach : Reach, window.name)) return false;
            if (a == Action.WindowBarricade && window.barricaded) { Authority.Refuse("the window is already barricaded"); return false; }
            if (a == Action.WindowDestroyBarricade && !window.barricaded) { Authority.Refuse("the window isn't barricaded"); return false; }
            if (a == Action.WindowHit && x > 1000f) { Authority.Refuse($"a hit of {x:0} is more than any weapon does"); return false; }
            return true;
        }

        /// <summary>
        /// Host: whether the lock's key is in the journal. Keys are journal entries, shared by FlagsSync, so a key a
        /// client's player picked up is in the host's journal too: the client sends its journal changes just before
        /// the unlock (LockShare.Send), on the same ordered channel.
        /// </summary>
        public static bool HasKey(Locked l)
        {
            var keys = Singleton<UI>.Instance?.journal?.keysDict;
            return l != null && keys != null && !string.IsNullOrEmpty(l.keyType) && keys.ContainsKey(l.keyType);
        }

        /// <summary>A crate whose lock is picked with the progress bar (Item.disarm without a Trigger), or null.</summary>
        public static Item Crate(Item item) =>
            item != null && item.disarmTime > 0f && item.GetComponent<Inventory>() != null && item.GetComponent<Trigger>() == null ? item : null;

        // ---- the state to roll back to ----------------------------------------------------

        private enum Kind : byte { Gone = 0, Door = 1, Window = 2, Trap = 3, Item = 4, Padlock = 5, Explosive = 6 }

        /// <summary>
        /// Host: tell a client how this thing really is (ObjState: ObjRef, u8 kind, then the kind's state), so it
        /// can put its copy back after a refused request.
        /// door: u8 flags (1 open, 2 barricaded, 4 broken, 8 locked), i32 health, i32 barricade health, str key type.
        /// window: u8 flags (1 barricaded), i32 barricade health. trap: u8 flags (1 armed).
        /// item: u8 flags (1 switched on, 2 locked, 4 searched, 8 broken, 16 its InteractiveItem is on, 32 its feeder is full), i32 health. padlock (when the request was
        /// about the padlock): u8 flags (1 locked). explosive (an Explodes that isn't an item): u8 flags (1 gone off).
        /// gone: nothing (it isn't in the host's world).
        /// </summary>
        public static void SendState(ulong to, ObjRef obj, bool padlock = false)
        {
            var w = S.Begin(MsgType.ObjState);
            obj.Write(w);
            var go = ObjRef.Resolve<Transform>(obj);
            Door door = go != null ? go.GetComponent<Door>() : null;
            Window window = go != null ? go.GetComponent<Window>() : null;
            Trigger trap = go != null ? go.GetComponent<Trigger>() : null;
            Item item = go != null ? go.GetComponent<Item>() : null;
            Padlock pad = padlock && go != null ? go.GetComponent<Padlock>() : null;
            if (pad != null) { w.W.Write((byte)Kind.Padlock); w.W.Write((byte)(pad.locked ? 1 : 0)); }
            else if (door != null)
            {
                var l = door.GetComponent<Locked>();
                w.W.Write((byte)Kind.Door);
                w.W.Write((byte)((door.opened ? 1 : 0) | (door.barricaded ? 2 : 0) | (door.destroyed ? 4 : 0) | (l != null && l.locked ? 8 : 0)));
                w.W.Write(door.health); w.W.Write(door.barricadeHealth);
                w.WriteStr(l != null ? l.keyType ?? "" : "");
            }
            else if (window != null)
            {
                w.W.Write((byte)Kind.Window);
                w.W.Write((byte)(window.barricaded ? 1 : 0));
                w.W.Write(window.barricadeHealth);
            }
            else if (trap != null) { w.W.Write((byte)Kind.Trap); w.W.Write((byte)(trap.active ? 1 : 0)); }
            else if (item != null)
            {
                var l = item.GetComponent<Locked>();
                w.W.Write((byte)Kind.Item);
                var ii = item.GetComponent<InteractiveItem>();
                var feeder = item.GetComponent<Feeder>();
                w.W.Write((byte)((item.isOn ? 1 : 0) | (l != null && l.locked ? 2 : 0) | (item.searched ? 4 : 0) | (item.destroyed ? 8 : 0) | (ii != null && ii.isOn ? 16 : 0) | (feeder != null && feeder.Active ? 32 : 0)));
                w.W.Write(item.health);
            }
            else if (go != null && go.GetComponent<Explodes>() != null)
            {
                w.W.Write((byte)Kind.Explosive);
                w.W.Write((byte)(BreakSync.Activated(go.GetComponent<Explodes>()) ? 1 : 0));
            }
            else w.W.Write((byte)Kind.Gone);
            S.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>Client: put our copy of this thing as the host has it (a refused request's rollback).</summary>
        private static void OnObjState(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId || !WorldShare.InWorld) return;
            var obj = ObjRef.Read(r);
            var kind = (Kind)r.R.ReadByte();
            var go = ObjRef.Resolve<Transform>(obj);
            Replaying = true;
            try
            {
                switch (kind)
                {
                    case Kind.Gone:
                        if (go != null && (go.GetComponent<Trigger>() != null || go.GetComponent<Item>() != null)) UnityEngine.Object.Destroy(go.gameObject);
                        break;
                    case Kind.Door:
                    {
                        byte f = r.R.ReadByte(); int health = r.R.ReadInt32(), barricade = r.R.ReadInt32(); string key = r.ReadStr();
                        var door = go != null ? go.GetComponent<Door>() : null;
                        if (door == null) break;
                        bool open = (f & 1) != 0, barricaded = (f & 2) != 0, broken = (f & 4) != 0, locked = (f & 8) != 0;
                        if (broken && !door.destroyed) door.destroyDoor(false);
                        else if (!broken && door.destroyed) door.unDestroy();
                        var l = door.GetComponent<Locked>();
                        if (locked && (l == null || !l.locked)) door.lockMe(key);
                        else if (!locked && l != null && l.locked) door.unlock();
                        if (barricaded && !door.barricaded) door.barricade(false);
                        else if (!barricaded && door.barricaded) door.destroyBarricade(false);
                        if (open && !door.opened) door.open(door.transform.position + door.transform.right, null, 0f);
                        else if (!open && door.opened) door.close(null);
                        door.health = health;
                        door.barricadeHealth = barricade;
                        break;
                    }
                    case Kind.Window:
                    {
                        bool barricaded = (r.R.ReadByte() & 1) != 0; int barricade = r.R.ReadInt32();
                        var window = go != null ? go.GetComponent<Window>() : null;
                        if (window == null) break;
                        if (barricaded && !window.barricaded) window.barricade(barricade, false);
                        else if (!barricaded && window.barricaded) window.destroyBarricade(false);
                        window.barricadeHealth = barricade;
                        break;
                    }
                    case Kind.Trap:
                    {
                        bool armed = (r.R.ReadByte() & 1) != 0;
                        var trap = go != null ? go.GetComponent<Trigger>() : null;
                        if (trap != null && trap.active != armed) trap.active = armed;
                        break;
                    }
                    case Kind.Item:
                    {
                        byte f = r.R.ReadByte(); int health = r.R.ReadInt32();
                        bool on = (f & 1) != 0, broken = (f & 8) != 0;
                        var item = go != null ? go.GetComponent<Item>() : null;
                        if (item == null) break;
                        if (item.isOn != on) { if (on) item.turnOn(); else item.turnOff(); }
                        var l = item.GetComponent<Locked>();
                        if (l != null) l.locked = (f & 2) != 0;
                        item.searched = (f & 4) != 0;
                        var ii = item.GetComponent<InteractiveItem>();
                        if (ii != null) ii.isOn = (f & 16) != 0;
                        var feeder = item.GetComponent<Feeder>();
                        if (feeder != null && feeder.Active != ((f & 32) != 0)) { if ((f & 32) != 0) feeder.makeActive(); else feeder.makeInactive(); }
                        if (broken && !item.destroyed && item.destructible) item.die();
                        else if (!broken && item.destroyed) item.setNormalState();
                        if (item != null) item.health = health;
                        break;
                    }
                    case Kind.Explosive:
                    {
                        r.R.ReadByte(); // an explosion here can't be taken back; the next state the host sends is how it is
                        break;
                    }
                    case Kind.Padlock:
                    {
                        bool locked = (r.R.ReadByte() & 1) != 0;
                        var pad = go != null ? go.GetComponent<Padlock>() : null;
                        if (pad != null && pad.locked != locked) { pad.locked = locked; if (locked) pad.currentCombination = "0000"; }
                        break;
                    }
                }
                if (Debug) Plugin.Log.LogInfo($"[world] the host's state of {obj} ({kind}) applied");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't apply the host's state of {obj}: {e.Message}"); }
            finally { Replaying = false; }
        }

        // ---- what changed ------------------------------------------------------

        public struct DoorState
        {
            public bool Opened, Barricaded, Destroyed; public int Health, BarricadeHealth;
            public static DoorState Of(Door d) => new DoorState { Opened = d.opened, Barricaded = d.barricaded, Destroyed = d.destroyed, Health = d.health, BarricadeHealth = d.barricadeHealth };
            public bool Same(DoorState o) => Opened == o.Opened && Barricaded == o.Barricaded && Destroyed == o.Destroyed && Health == o.Health && BarricadeHealth == o.BarricadeHealth;
        }

        public struct WindowState
        {
            public bool Barricaded; public int BarricadeHealth;
            public static WindowState Of(Window w) => new WindowState { Barricaded = w.barricaded, BarricadeHealth = w.barricadeHealth };
            public bool Same(WindowState o) => Barricaded == o.Barricaded && BarricadeHealth == o.BarricadeHealth;
        }
    }

    // ---- the calls -----------------------------------------------------------------

    [HarmonyPatch(typeof(Door), nameof(Door.open))]
    static class DoorOpenPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, Vector3 openerPosition, Transform openerTransform, WorldActions.DoorState __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __state.Same(WorldActions.DoorState.Of(__instance))) return;
            var at = openerTransform != null ? openerTransform.position : openerPosition;
            WorldActions.Send(WorldActions.Action.DoorOpen, __instance.gameObject, at.x, at.z);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.close))]
    static class DoorClosePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, WorldActions.DoorState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.DoorState.Of(__instance))) WorldActions.Send(WorldActions.Action.DoorClose, __instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.barricade))]
    static class DoorBarricadePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, bool byPlayer, WorldActions.DoorState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.DoorState.Of(__instance))) WorldActions.Send(WorldActions.Action.DoorBarricade, __instance.gameObject, f1: byPlayer);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyBarricade))]
    static class DoorDestroyBarricadePatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, bool silent, WorldActions.DoorState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.DoorState.Of(__instance))) WorldActions.Send(WorldActions.Action.DoorDestroyBarricade, __instance.gameObject, f1: silent);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.destroyDoor))]
    static class DoorDestroyPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, bool silently, WorldActions.DoorState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.DoorState.Of(__instance))) WorldActions.Send(WorldActions.Action.DoorDestroy, __instance.gameObject, f1: silently);
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unDestroy))]
    static class DoorUndestroyPatch
    {
        static void Prefix(Door __instance, out WorldActions.DoorState __state) { WorldActions.Depth++; __state = WorldActions.DoorState.Of(__instance); }
        static void Postfix(Door __instance, WorldActions.DoorState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.DoorState.Of(__instance))) WorldActions.Send(WorldActions.Action.DoorUndestroy, __instance.gameObject);
        }
    }

    /// <summary>
    /// Damage to a door or its barricade. Only the hit is sent (not what it causes): the other games make the same
    /// call, which takes off the same health and breaks the barricade or door when it runs out, as it does here.
    /// </summary>
    [HarmonyPatch(typeof(Door), nameof(Door.getHit), new[] { typeof(int), typeof(Transform), typeof(bool), typeof(bool) })]
    static class DoorHitPatch
    {
        /// <summary>On a client, a hit the host makes too (an explosion, fire) isn't made here: the host's DoorHit is.</summary>
        static bool Prefix(Door __instance, out WorldActions.DoorState __state)
        {
            WorldActions.Depth++;
            __state = WorldActions.DoorState.Of(__instance);
            return !BreakSync.HostsHit();
        }
        static void Postfix(Door __instance, int damage, bool normalHit, bool canDamageMetal, WorldActions.DoorState __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __state.Same(WorldActions.DoorState.Of(__instance))) return;
            WorldActions.Send(WorldActions.Action.DoorHit, __instance.gameObject, damage, f1: normalHit, f2: canDamageMetal, byPlayerKnown: BreakSync.OwnFlame());
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.lockMe))]
    static class DoorLockPatch
    {
        static void Prefix() => WorldActions.Depth++;
        static void Postfix(Door __instance, string keyType) { if (WorldActions.Outermost() && WorldActions.Sharing) WorldActions.Send(WorldActions.Action.DoorLock, __instance.gameObject, str: keyType); }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.unlock))]
    static class DoorUnlockPatch
    {
        static void Prefix() => WorldActions.Depth++;
        static void Postfix(Door __instance) { if (WorldActions.Outermost() && WorldActions.Sharing) WorldActions.Send(WorldActions.Action.DoorUnlock, __instance.gameObject); }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.barricade))]
    static class WindowBarricadePatch
    {
        static void Prefix(Window __instance, out WorldActions.WindowState __state) { WorldActions.Depth++; __state = WorldActions.WindowState.Of(__instance); }
        static void Postfix(Window __instance, int destHealth, bool byPlayer, WorldActions.WindowState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.WindowState.Of(__instance))) WorldActions.Send(WorldActions.Action.WindowBarricade, __instance.gameObject, destHealth, f1: byPlayer);
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.destroyBarricade))]
    static class WindowDestroyBarricadePatch
    {
        static void Prefix(Window __instance, out WorldActions.WindowState __state) { WorldActions.Depth++; __state = WorldActions.WindowState.Of(__instance); }
        static void Postfix(Window __instance, bool silent, WorldActions.WindowState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.WindowState.Of(__instance))) WorldActions.Send(WorldActions.Action.WindowDestroyBarricade, __instance.gameObject, f1: silent);
        }
    }

    [HarmonyPatch(typeof(Window), nameof(Window.getHit), new[] { typeof(int), typeof(Transform), typeof(bool) })]
    static class WindowHitPatch
    {
        /// <summary>On a client, a hit the host makes too (an explosion, fire) isn't made here: the host's WindowHit is.</summary>
        static bool Prefix(Window __instance, out WorldActions.WindowState __state)
        {
            WorldActions.Depth++;
            __state = WorldActions.WindowState.Of(__instance);
            return !BreakSync.HostsHit();
        }
        static void Postfix(Window __instance, int damage, bool normalHit, WorldActions.WindowState __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && !__state.Same(WorldActions.WindowState.Of(__instance)))
                WorldActions.Send(WorldActions.Action.WindowHit, __instance.gameObject, damage, f1: normalHit, byPlayerKnown: BreakSync.OwnFlame());
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// A trap going off. In each game its own player sets traps off as they step on them; the host's enemies set
    /// them off on the host only (a client's traps ignore characters: the ones it shows are the host's, moved by
    /// the host, and their traps went off there). Who saw it go off tells the others, who set theirs off too:
    /// its sound, what it spawns (an explosion hurts the host's enemies on the host), and its sprung look.
    /// A client tells only of traps its own player (or something it threw) set off.
    ///
    /// A client's own trap is a request the host may refuse (it had gone off there already, or isn't there). So
    /// on a client the trap goes off at once (its sound, its sprung look, the bear trap's bite and hold), but what
    /// it spawns (Trigger.prefabToSpawn: a vine trap's vine) waits for the host's yes, as a feeder's effect does:
    /// a spawned thing is shared world, and a vine that already holds the player can't be taken back cleanly. If
    /// the host refuses, nothing is spawned and the bite is undone (the hold released, the health it took given
    /// back). If the host's own TrapFired for the same trap comes first (it went off there just before), that one
    /// spawns it, untargeted, as any replay of the host's trap does.
    /// </summary>
    [HarmonyPatch(typeof(Trigger), "OnAfterTrigger")]
    static class TrapFiredPatch
    {
        public struct State { public UnityEngine.Object Held; }

        /// <summary>A client's trap spawn waiting for the host's yes.</summary>
        private sealed class Held
        {
            public UnityEngine.Object Prefab;
            public Vector3 At;
            public GameObject Target; // a vine trap's: who its vine holds
            public bool Done;
        }
        private static readonly System.Collections.Generic.Dictionary<string, Held> _held = new System.Collections.Generic.Dictionary<string, Held>();

        /// <summary>Client: the player's health before the trap they stepped on bit them (ClientTrapBitePatch).</summary>
        internal static float HealthBefore = -1f;

        /// <summary>
        /// Tests only (the map step "trap-refuse"): the host refuses the next TrapFired requests, having first set the
        /// trap off itself ("race") or taken it away ("gone"). Null: off.
        /// </summary>
        public static string TestRefuse;

        static void Prefix(Trigger __instance, Collider other, out State __state)
        {
            WorldActions.Depth++;
            __state = default;
            if (WorldActions.Depth != 1 || !WorldActions.Sharing || !Net.Session.Instance.IsClient || __instance.loadedFromSave) return;
            if (__instance.prefabToSpawn == null || !Ours(__instance, other)) return;
            __state.Held = __instance.prefabToSpawn;
            __instance.prefabToSpawn = null; // nonPooledPrefabToSpawn alone spawns nothing (Trigger.OnAfterTrigger)
        }

        static void Postfix(Trigger __instance, Collider other, bool doConnectChain, State __state)
        {
            if (__state.Held != null && __instance != null) __instance.prefabToSpawn = __state.Held; // pooled: the next one spawns again
            float healthBefore = HealthBefore; HealthBefore = -1f;
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null) return;
            if (!Ours(__instance, other)) return;
            var s = Net.Session.Instance;
            if (s.IsHost) { WorldActions.Send(WorldActions.Action.TrapFired, __instance.gameObject, byPlayerKnown: true); return; }

            var r = ObjRef.Of(__instance.gameObject);
            if (!r.Valid) return;
            string key = r.ToString();
            var held = __state.Held == null ? null : new Held
            {
                Prefab = __state.Held,
                At = __instance.transform.position + new Vector3(0f, 1f, 0f),
                Target = __instance.isChainTrap && doConnectChain && other != null ? other.gameObject : null,
            };
            if (held != null) _held[key] = held;
            var player = other != null ? other.GetComponent<Player>() : null;
            float lost = player != null && healthBefore > 0f ? healthBefore - player.health : 0f;
            bool bearHold = player != null && __instance.isBearTrap;

            var w = s.Begin(MsgType.WorldAction);
            w.W.Write((byte)WorldActions.Action.TrapFired);
            r.Write(w);
            w.W.Write(0f); w.W.Write(0f); w.W.Write((byte)0); w.WriteStr("");
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent TrapFired {r} ({__instance.name}){(held != null ? ", its " + held.Prefab.name + " waits for the host" : "")}");
            Authority.Request(w, $"TrapFired {r} ({__instance.name})", (ok, reason, result) =>
            {
                if (held != null && _held.TryGetValue(key, out var h) && h == held) _held.Remove(key);
                if (ok) { if (held != null) Spawn(held, key); return; }
                if (held != null && !held.Done) Plugin.Log.LogInfo($"[world] TrapFired {key}: refused, so its {held.Prefab.name} isn't spawned");
                if (player == null || player != Player.Instance) return;
                if (bearHold && player.inBearTrap) player.stopAllPerformingActionAnims();
                if (lost > 0f && player.health > 0f) player.health = Mathf.Min(player.maxHealth, player.health + lost);
                if (bearHold || lost > 0f) Plugin.Log.LogInfo($"[world] TrapFired {key}: refused, so the bite is undone (hold released, {lost:0} health back)");
            }, inventory: false);
        }

        /// <summary>Client, applying the host's TrapFired: if our own firing of it holds a spawn, it's spawned now, for the host's.</summary>
        internal static bool SpawnHeld(ObjRef r)
        {
            string key = r.ToString();
            if (!_held.TryGetValue(key, out var held)) return false;
            _held.Remove(key);
            held.Target = null; // as the host's replay does: it went off there, not on our player
            Spawn(held, key);
            return true;
        }

        private static void Spawn(Held held, string key)
        {
            if (held.Done) return;
            held.Done = true;
            var go = Core.AddPrefab(held.Prefab, held.At, Quaternion.Euler(90f, 0f, 0f), null);
            if (go == null) return;
            if (held.Target != null) { var cp = go.GetComponent<ChainParent>(); if (cp != null) cp.target = held.Target; }
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] TrapFired {key}: spawned {held.Prefab.name}");
        }

        /// <summary>A trap set off by this game's own player or something it threw (on a client, only those are sent).</summary>
        private static bool Ours(Trigger trap, Collider other)
        {
            if (!trap.setByPlayer && trap.GetComponent<Item>() == null) return false; // a level's scripted trigger zone, not a trap
            if (!Net.Session.Instance.IsClient) return true;
            return other != null && (other.GetComponent<Player>() != null || other.GetComponent<ThrownItem>() != null);
        }

        internal static void Reset() => _held.Clear();
    }

    /// <summary>Client: notes the player's health before a trap bites, so a refused trap can give it back (TrapFiredPatch).</summary>
    [HarmonyPatch(typeof(Trigger), nameof(Trigger.checkCollision))]
    static class ClientTrapBitePatch
    {
        static void Prefix(Collider other)
        {
            if (!Combat.NetEnemies.ClientMode || other == null || Player.Instance == null) return;
            if (other.GetComponent<Player>() != null) TrapFiredPatch.HealthBefore = Player.Instance.health;
        }
        static void Postfix() => TrapFiredPatch.HealthBefore = -1f;
    }

    /// <summary>Client: traps don't go off on characters here (see TrapFiredPatch).</summary>
    [HarmonyPatch(typeof(Trigger), nameof(Trigger.checkCollision))]
    static class ClientTrapIgnoresCharactersPatch
    {
        static bool Prefix(Collider other)
        {
            if (!Combat.NetEnemies.ClientMode || other == null) return true;
            var c = other.GetComponent<Character>();
            if (c == null) return true;
            // The host's enemies (and our frozen copies) set traps off on the host; ones of an area of our own do here.
            return !(Combat.NetEnemies.Frozen.Contains(c) || (Combat.NetEnemies.Instance != null && Combat.NetEnemies.Instance.IsPuppet(c, out _)));
        }
    }

    /// <summary>A trap disarmed (or failed to be): it's taken away, or left sprung, for everyone.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.switchTriggerState))]
    static class TrapSwitchedPatch
    {
        static void Prefix() => WorldActions.Depth++;
        static void Postfix(Item __instance)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && __instance != null && __instance.GetComponent<Trigger>() != null)
                WorldActions.Send(WorldActions.Action.TrapSwitched, __instance.gameObject);
        }
    }

    /// <summary>Lamps, generators, radios...: switched on or off for everyone.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class ItemOnPatch
    {
        static void Prefix() => WorldActions.Depth++;
        static void Postfix(Item __instance) { if (WorldActions.Outermost() && WorldActions.Sharing) WorldActions.Send(WorldActions.Action.ItemOn, __instance.gameObject); }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.turnOff))]
    static class ItemOffPatch
    {
        static void Prefix() => WorldActions.Depth++;
        static void Postfix(Item __instance) { if (WorldActions.Outermost() && WorldActions.Sharing) WorldActions.Send(WorldActions.Action.ItemOff, __instance.gameObject); }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// An NPC (a trader...) died. NPCs live in each player's game; whoever's game it died in tells the others, and
    /// it dies there too. (The game marks some deaths only with a story flag, which FlagsSync shares as well.)
    /// A client's NPCs can only be killed by its own player or the story, so its NPC deaths are always news.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.die))]
    static class NpcDiedPatch
    {
        static void Prefix(Character __instance, out bool __state) { WorldActions.Depth++; __state = __instance.alive; }
        static void Postfix(Character __instance, bool __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null || __instance.npc == null || !__state) return;
            // With its name and where it stood: one each game puts in place itself (the morning trader, the wolf)
            // has its own save id in each, and one that walked can be off its named spot.
            var p = __instance.transform.position;
            WorldActions.Send(WorldActions.Action.NpcDied, __instance.gameObject, x: p.x, y: p.z, str: __instance.npc.name, byPlayerKnown: true);
        }
    }

    /// <summary>
    /// Killing the night trader sends the killer into a dream (`Character.die`'s isNightTrader branch calls
    /// `Player.special_onKillNightTrader`). When the death is replayed from another game, that dream belongs to the
    /// killer, not to us: skip it. The branch's flag (nightTrader_killedYesterday) and `endAfterNight` still run.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.special_onKillNightTrader))]
    static class NightTraderDreamPatch
    {
        static bool Prefix()
        {
            if (!WorldActions.Replaying) return true;
            Plugin.Log.LogInfo("[world] the night trader was killed in another game: the killer's dream isn't ours");
            return false;
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// Client: disarming a shared trap is the host's roll. When our player's progress bar for it completes, the
    /// game would roll here (disarmChance against a random number) and disarm it or set it off. Instead the trap
    /// goes to the host as a TrapDisarm request; the host rolls with its own random numbers and says how it went
    /// (Verdict result "1" or "0"), and then the game's own disarm (the item comes to our player) or failDisarm
    /// runs here, with its message. While the host decides, the trap stays as it is.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.progressBarCompleted))]
    static class ClientDisarmPatch
    {
        private static readonly AccessTools.FieldRef<Player, Item> Trap = AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");

        static void Prefix(Player __instance)
        {
            if (!__instance.disarmingTrap || !NetEnemies.ClientMode || !WorldActions.Sharing) return;
            var trap = Trap(__instance);
            if (trap == null) return;
            var r = ObjRef.Of(trap.gameObject);
            if (!r.Valid) return; // not a shared trap: the game rolls it, as alone
            __instance.disarmingTrap = false; // the game's own roll is skipped
            // The crouch co-op adds (DisarmAnimation) ends here too: with disarmingTrap already cleared, the
            // stand-up in DisarmDonePatch may not see that a disarm ended, which left our player crouched for good.
            DisarmAnimation.StandUp(__instance);
            var trigger = trap.GetComponent<Trigger>();
            if (trigger != null && trigger.bendDownToDisarm) { __instance.hidingInventory = true; __instance.gotInventory = false; }

            var s = Session.Instance;
            var w = s.Begin(MsgType.WorldAction);
            w.W.Write((byte)WorldActions.Action.TrapDisarm);
            r.Write(w);
            w.W.Write(0f); w.W.Write(0f); w.W.Write((byte)0); w.WriteStr("");
            Authority.Request(w, $"TrapDisarm {r} ({trap.name})", (ok, reason, result) =>
            {
                if (trap == null || Player.Instance == null) return;
                WorldActions.Replaying = true;
                try
                {
                    if (ok && result == "1")
                    {
                        trap.disarm();
                        Player.Instance.displayMessage(Language.Get("Playermsg_disarmedSuccess", "UI"));
                    }
                    else if (ok)
                    {
                        Player.Instance.displayMessage(Language.Get("Playermsg_disarmedFailure", "UI"));
                        trap.failDisarm();
                    }
                    // refused: the trap was gone or had gone off on the host; ObjState put ours as it is there
                }
                finally { WorldActions.Replaying = false; }
            });
        }
    }
}

namespace DarkwoodMP.World
{
    /// <summary>
    /// Locks opened with a key or a lockpick (Locked.tryToOpenWithKey / tryToLockpick, which call Locked.unlock).
    /// Locked.unlock only clears Locked.locked and never goes through Door.unlock, so it's patched itself: a door's
    /// lock goes as DoorUnlock, any other's (a chest, a cabinet) as ItemUnlock, with a = 1 for a key, 2 for a
    /// lockpick, 0 for anything else (the story). The two try methods are patched too, so the outermost call is
    /// the one sent: tryToLockpick takes the lockpick after unlocking, and a refused request must give it back.
    /// </summary>
    static class LockShare
    {
        public static void Send(Locked l, float how)
        {
            // With a key: the host checks the key is in its journal, so our journal's news (the key just picked
            // up) goes first.
            if (how == 1f && Net.Session.Instance != null && Net.Session.Instance.IsClient) FlagsSync.Instance?.FlushNow();
            var door = l.GetComponent<Door>();
            if (door != null) WorldActions.Send(WorldActions.Action.DoorUnlock, door.gameObject, how);
            else WorldActions.Send(WorldActions.Action.ItemUnlock, l.gameObject, how);
        }
    }

    [HarmonyPatch(typeof(Locked), nameof(Locked.unlock))]
    static class LockedUnlockPatch
    {
        static void Prefix(Locked __instance, out bool __state) { WorldActions.Depth++; __state = __instance.locked; }
        static void Postfix(Locked __instance, bool __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && __state && !__instance.locked) LockShare.Send(__instance, 0f);
        }
    }

    [HarmonyPatch(typeof(Locked), nameof(Locked.tryToOpenWithKey))]
    static class LockedKeyPatch
    {
        static void Prefix(Locked __instance, out bool __state) { WorldActions.Depth++; __state = __instance.locked; }
        static void Postfix(Locked __instance, bool __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && __state && !__instance.locked) LockShare.Send(__instance, 1f);
        }
    }

    [HarmonyPatch(typeof(Locked), nameof(Locked.tryToLockpick))]
    static class LockedLockpickPatch
    {
        static void Prefix(Locked __instance, out bool __state) { WorldActions.Depth++; __state = __instance.locked; }
        static void Postfix(Locked __instance, bool __state)
        {
            if (WorldActions.Outermost() && WorldActions.Sharing && __state && !__instance.locked) LockShare.Send(__instance, 2f);
        }
    }

    /// <summary>
    /// A locked crate picked open in the host's game (Item.disarm on an item with no Trigger sets searched and
    /// opens it): the others are told it's searched, so nobody has to pick it again. A client's picks go to the
    /// host as TrapDisarm requests instead (ClientDisarmPatch), and the host sends ItemSearched itself. Not a
    /// Depth call: a trap's disarm goes through switchTriggerState, which must stay the outermost call there.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.disarm))]
    static class CrateSearchedPatch
    {
        static void Prefix(Item __instance, out bool __state) => __state = __instance != null && __instance.searched;
        static void Postfix(Item __instance, bool __state)
        {
            if (__instance == null || __state || !__instance.searched || WorldActions.Crate(__instance) == null) return;
            if (WorldActions.Depth == 0 && WorldActions.Sharing && Session.Instance.IsHost)
                WorldActions.Send(WorldActions.Action.ItemSearched, __instance.gameObject);
        }
    }

    /// <summary>
    /// A container opened the game's way (Item.openInventory: by the player, or after a crate's lock was picked)
    /// sets Item.searched (and Character.searched on a corpse) in the opener's game only; it's saved and changes the
    /// hover text. Send it as ItemSearched when it changed. A client sends only its own player's open (Send checks
    /// ByLocalPlayer; openInventory uses Player.Instance, so it's always the local player's), and the host checks
    /// reach. A picked crate is already searched when disarm calls openInventory, so this sends nothing for it.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.openInventory))]
    static class OpenSearchedPatch
    {
        static void Prefix(Item __instance, out bool __state) => __state = __instance != null && __instance.searched;
        static void Postfix(Item __instance, bool __state)
        {
            if (__instance == null || __state || !__instance.searched || !WorldActions.Sharing) return;
            WorldActions.Send(WorldActions.Action.ItemSearched, __instance.gameObject);
        }
    }

    /// <summary>
    /// A combination padlock opened (Padlock.unlock, after checkCombination, or by the story with manually false).
    /// The others' copies are unlocked without the window or events (Padlock.unlock(false)). A client's player
    /// sends the code it entered; the host checks it and the reach, and runs the padlock's onUnlockPadlock events
    /// in its own game (see WorldActions.OnAction), so what they change is shared from there.
    /// </summary>
    [HarmonyPatch(typeof(Padlock), nameof(Padlock.unlock), new[] { typeof(bool) })]
    static class PadlockUnlockPatch
    {
        static void Prefix(Padlock __instance, out bool __state) { WorldActions.Depth++; __state = __instance.locked; }
        static void Postfix(Padlock __instance, bool manually, bool __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || !__state || __instance.locked) return;
            WorldActions.Send(WorldActions.Action.PadlockUnlock, __instance.gameObject, f1: manually, str: manually ? __instance.currentCombination : "");
        }
    }
}
