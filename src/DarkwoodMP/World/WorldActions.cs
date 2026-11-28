using DarkwoodMP.World.Dreaming;
using System;
using System.Collections.Generic;
using System.Reflection;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using DarkwoodMP.World.Scripted;
using DarkwoodMP.World.Windows;
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
    public static partial class WorldActions
    {

        /// <summary>
        /// "--dwmp-debug-world": log every shared-world message sent and
        /// applied (ModHost.DebugWorld).
        /// </summary>
        public static bool Debug => Plugin.Instance.Host.DebugWorld;

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

        public static bool ByLocalPlayer()
        {
            if (Plugin.Instance.Host.TestAsPlayer)
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
            string shared = null;
            if (Coop.Current != null)
            {
                shared = Coop.Current.Dreaming.Shared;
            }

            if (d != null && shared == null
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
        /// are. Null: only where our player is counts. On the host too: a
        /// thing in a later part of the epilogue (InLaterPart), whose doings
        /// stay in each game.
        /// </summary>
        public static bool OursAlone(GameObject go)
        {
            var s = S;
            if (s == null)
            {
                return false;
            }

            if (go != null && InLaterPart(go))
            {
                return true;
            }

            if (!s.IsClient)
            {
                return false;
            }

            if (go == null)
            {
                return InOwnArea();
            }

            var d = Singleton<Dreams>.Instance;
            string shared = null;
            if (Coop.Current != null)
            {
                shared = Coop.Current.Dreaming.Shared;
            }

            if (shared != null && d != null
                && d.dreamLocation != null
                && (d.dreaming || d.dreamPrepared || d.switchingDream)
                && (d.preset == null || d.preset.name == shared)
                && go.transform.IsChildOf(d.dreamLocation.transform))
            {
                return false;
            }

            return NetEnemies.LocalOnly(go) || InOwnArea();
        }

        /// <summary>
        /// This thing is in the later part of the epilogue our player is in
        /// (Whereabouts.LaterPart), in any game. Null: our player is in one.
        /// Each game builds such a part for itself; what is done there isn't
        /// sent, and the world's own triggers there run in each game.
        /// </summary>
        public static bool InLaterPart(GameObject go)
        {
            var part = Places.Whereabouts.LaterPart();
            if (part == null)
            {
                return false;
            }

            if (go == null)
            {
                return true;
            }

            return go.transform.IsChildOf(part.transform);
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

            if (S.IsClient && Coop.Current.Hammer.Striking)
            {
                Coop.Current.Hammer.Hold(
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
    }
}
