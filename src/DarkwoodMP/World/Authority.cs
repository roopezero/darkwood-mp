using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The host is the authority for everything a client's player does to the shared world. The client does it
    /// at once in its own game, as a prediction, and sends it as a Request; the host checks it against its own
    /// world, makes it there (rolling any chance itself) and answers with a Verdict. A refused request is rolled
    /// back in the client's game.
    ///
    /// Request (client to host): u32 seq, then a whole message (its type byte and payload), which the host
    /// handles with that message's own handler. The handler may refuse it (Refuse), and may say what came of it
    /// (Result, for things decided on the host: the damage dealt, whether a disarm worked).
    ///
    /// Verdict (host to client): u32 seq, u8 ok, str reason (why it was refused), str result.
    ///
    /// Rolling back a refused request: before the Verdict, the host sends the state of what it was about as it
    /// really is (ObjState, from the handler); and the client undoes what its own inventory gained or lost in the
    /// frame it did it (items it used up come back, items it got go again). This is the client-side half of
    /// server reconciliation: the client predicts, the host decides, and a wrong prediction is replaced by the
    /// host's state.
    ///
    /// It isn't rollback in the fighting-game sense (re-simulating frames from the last confirmed input): that
    /// needs a deterministic game that can save and restore its whole state every frame, which Darkwood can't.
    /// </summary>
    public static class Authority
    {
        private static Session S => Session.Instance;

        // ---- client ---------------------------------------------------------------------------------

        private sealed class Pending
        {
            public string What;
            public float At;
            public Dictionary<string, int> Inventory;
            public Action<bool, string, string> Done;
        }

        private static uint _seq;
        private static readonly Dictionary<uint, Pending> _pending = new Dictionary<uint, Pending>();
        private static readonly byte[] _copy = new byte[1 << 16];

        /// <summary>Requests sent and not answered yet.</summary>
        public static int Waiting => _pending.Count;
        /// <summary>Requests the host refused (and that were rolled back) this session.</summary>
        public static int RolledBack { get; private set; }

        public static void Register(Session s)
        {
            s.Register(MsgType.Request, OnRequest);
            s.Register(MsgType.Verdict, OnVerdict);
            s.AllowBare = AllowBare;
            s.OnSessionEnded += EndPending;
        }

        /// <summary>
        /// The session ended with requests unanswered: tell each caller it was refused, so state kept until the Verdict
        /// (a button waiting for the host, a disarm in progress) is let go. The inventory isn't undone: the host's world
        /// is gone, and the caller's own rollback is all that runs.
        /// </summary>
        private static void EndPending()
        {
            var left = new List<Pending>(_pending.Values);
            _pending.Clear();
            _bareWarned.Clear();
            foreach (var p in left)
            {
                try
                {
                    Action<bool, string, string> handler = p.Done;
                    if (handler != null)
                    {
                        handler(false, "the session ended", "");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[authority] after {p.What}: {e.Message}");
                }
            }
            InventoryWatch.Reset();
        }

        /// <summary>
        /// Messages an honest client sends only inside a Request: their handlers check them only there (InReach, Refuse),
        /// so the host drops one that comes from a client on its own.
        /// </summary>
        private static readonly HashSet<MsgType> RequestOnly = new HashSet<MsgType>
        {
            MsgType.WorldAction, MsgType.EnemyHit, MsgType.NpcHit, MsgType.NpcAttack, MsgType.Burning,
            MsgType.Construct, MsgType.EventFired, MsgType.StoryRequest, MsgType.EggDestroyed, MsgType.InfectionCleared,
            MsgType.TraderTrade, MsgType.StorageSort, MsgType.SkillSpend, MsgType.EssenceAdd, MsgType.SkillReset,
            MsgType.PorterTransport, MsgType.PorterCalled, MsgType.OvenLit, MsgType.Fuel, MsgType.DragClaim,
        };
        private static readonly HashSet<(ulong, MsgType)> _bareWarned = new HashSet<(ulong, MsgType)>();

        /// <summary>Host (Session.Dispatch): whether a client's message may be handled as it came.</summary>
        private static bool AllowBare(ulong from, MsgType type)
        {
            if (Handling || !RequestOnly.Contains(type))
            {
                return true;
            }
            if (_bareWarned.Add((from, type)))
            {
                Plugin.Log.LogWarning($"[authority] dropped {type} from {S?.NameOf(from) ?? "#" + from}: it's only taken as a request");
            }
            return false;
        }

        /// <summary>
        /// Client: send a message the host must agree to. what: for the log. done(ok, reason, result) runs when the
        /// Verdict comes, after any rollback. The inventory change this frame goes with it, to undo if refused (unless
        /// inventory is false: for a request that isn't about items, made from a coroutine at any moment).
        /// </summary>
        public static void Request(PacketWriter message, string what, Action<bool, string, string> done = null, bool inventory = true)
        {
            var s = S;
            if (s == null || !s.IsClient)
            {
                return;
            }
            int n = message.Length;
            if (n > _copy.Length)
            {
                Plugin.Log.LogWarning($"Request too long: {what}");
                return;
            }
            Buffer.BlockCopy(message.Buffer, 0, _copy, 0, n);
            uint seq = ++_seq;
            _pending[seq] = new Pending { What = what, At = Time.realtimeSinceStartup, Inventory = inventory ? InventoryWatch.TakeChange() : null, Done = done };
            var w = s.Begin(MsgType.Request);
            w.W.Write(seq);
            w.W.Write(_copy, 0, n);
            s.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[authority] request {seq}: {what}");
            }
        }

        private static void OnVerdict(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId)
            {
                return;
            }
            uint seq = r.R.ReadUInt32();
            bool ok = r.R.ReadByte() != 0;
            string reason = r.ReadStr();
            string result = r.ReadStr();
            if (!_pending.TryGetValue(seq, out var p))
            {
                return;
            }
            _pending.Remove(seq);
            if (!ok)
            {
                RolledBack++;
                Plugin.Log.LogInfo($"[authority] the host refused {p.What}: {reason}; rolled back");
                InventoryWatch.Undo(p.Inventory);
            }
            else if (WorldActions.Debug)
            {
                string text = "";
                if (result.Length > 0)
                {
                    text = " (" + result + ")";
                }
                Plugin.Log.LogInfo($"[authority] {p.What}: ok{text} after {(Time.realtimeSinceStartup - p.At) * 1000f:0} ms");
            }
            try
            {
                Action<bool, string, string> handler = p.Done;
                if (handler != null)
                {
                    handler(ok, reason, result);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[authority] after {p.What}: {e.Message}");
            }
        }

        // ---- host -----------------------------------------------------------------------------------

        /// <summary>Host: true while handling a client's Request (its handler may Refuse it or set a Result).</summary>
        public static bool Handling { get; private set; }
        /// <summary>Host: who sent the request being handled.</summary>
        public static ulong From { get; private set; }
        private static string _refusal, _result;

        /// <summary>Host, in a request's handler: refuse it. Send the client the state it should roll back to first.</summary>
        public static void Refuse(string reason)
        {
            if (Handling && _refusal == null)
            {
                _refusal = reason;
            }
        }
        public static bool Refused => _refusal != null;
        /// <summary>Host, in a request's handler: what came of it, for the client (decided here: damage, a roll).</summary>
        public static void Result(string result)
        {
            if (Handling)
            {
                string result2 = result;
                if (result2 == null)
                {
                    result2 = "";
                }
                _result = result2;
            }
        }

        private static void OnRequest(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost)
            {
                return;
            }
            uint seq = r.R.ReadUInt32();
            byte[] inner = r.R.ReadBytes(1 << 16);
            Handling = true;
            From = from;
            _refusal = null;
            _result = "";
            try
            {
                if (inner.Length == 0 || (MsgType)inner[0] == MsgType.Request)
                {
                    Refuse("empty request");
                }
                else
                {
                    s.Handle(from, inner, inner.Length);
                }
            }
            catch (Exception e)
            {
                Refuse("error: " + e.Message);
            }
            finally
            {
                Handling = false;
            }
            var w = s.Begin(MsgType.Verdict);
            w.W.Write(seq);
            int value = 0;
            if (_refusal == null)
            {
                value = 1;
            }
            w.W.Write((byte)value);
            string refusal = _refusal;
            if (refusal == null)
            {
                refusal = "";
            }
            w.WriteStr(refusal);
            string result = _result;
            if (result == null)
            {
                result = "";
            }
            w.WriteStr(result);
            s.SendTo(from, w, Channel.Control, reliable: true);
            if (_refusal != null)
            {
                Plugin.Log.LogInfo($"[authority] refused {(inner.Length > 0 ? ((MsgType)inner[0]).ToString() : "nothing")} from {s.NameOf(from)}: {_refusal}");
            }
        }

        /// <summary>Host: the proxy (the host's stand-in) of the player whose request is being handled.</summary>
        public static NetPlayerProxy Requester
        {
            get
            {
                if (Handling && NetPlayerProxy.All.TryGetValue(From, out var p))
                {
                    return p;
                }
                return null;
            }
        }

        /// <summary>
        /// Host, in a request's handler: the requesting player is somewhere the host's game doesn't run (their
        /// stand-in is Away: a dream of their own, a separate location the host hasn't got at their spot). What they
        /// did there isn't in the host's world, so it can't be checked here, nor refused.
        /// </summary>
        public static bool RequesterAway => Requester is NetPlayerProxy p && p != null && p.Away;

        /// <summary>
        /// Host, in a request's handler: take back a refusal made while checking it, because the request is about a
        /// place only the requester has (RequesterAway). It is accepted and not applied here; result tells the
        /// requester so (ScriptedEvents: "alone" runs the event's every part there).
        /// </summary>
        public static void AcceptAway(string what, string result = "")
        {
            if (!Handling)
            {
                return;
            }
            Plugin.Log.LogInfo($"[authority] {S.NameOf(From)}'s {what}: they're where the host doesn't run ({_refusal ?? "not here"}), so it's theirs alone: accepted, not applied here");
            _refusal = null;
            string result2 = result;
            if (result2 == null)
            {
                result2 = "";
            }
            _result = result2;
        }

        /// <summary>Host: whether this player's stand-in is within reach of pos (true if it isn't known yet).</summary>
        public static bool Near(ulong player, Vector3 pos, float reach)
        {
            if (!NetPlayerProxy.All.TryGetValue(player, out var p) || p == null)
            {
                return true;
            }
            return Flat(p, pos) <= reach;
        }

        /// <summary>Host, in a request's handler: refuse it if the requesting player is further than reach from pos.</summary>
        public static bool InReach(Vector3 pos, float reach, string what)
        {
            var p = Requester;
            if (p == null)
            {
                return true; // not known yet (just joined): nothing to check against
            }
            float d = Flat(p, pos);
            // The stand-in follows the puppet, which moves on the next frame; the player's newest position (a
            // PlayerState flushed just before this request, Sync.PlayerSync.FlushNow) has already been through
            // MoveCheck, so it's as trustworthy as the stand-in and newer. Either will do.
            if (d > reach && Sync.PlayerSync.Instance != null && Sync.PlayerSync.Instance.TryChecked(From, out var at))
            {
                d = Mathf.Min(d, new Vector2(at.x - pos.x, at.z - pos.z).magnitude);
            }
            if (d <= reach)
            {
                return true;
            }
            Refuse($"{what} is {d:0} away from them (reach {reach:0})");
            return false;
        }

        private static float Flat(NetPlayerProxy p, Vector3 pos)
        {
            var d = p.transform.position - pos;
            return new Vector2(d.x, d.z).magnitude;
        }
    }

    /// <summary>
    /// What the local player carries, by item type, as it was at the end of the last frame: what a request's
    /// frame added or used up is the difference, which a refused request gives back or takes away again.
    /// </summary>
    public static class InventoryWatch
    {
        private static Dictionary<string, int> _base;

        public static void Reset() => _base = null;

        /// <summary>Every frame (late): remember what the player carries now.</summary>
        public static void LateUpdate()
        {
            if (!NetEnemies.ClientMode || !WorldShare.InWorld)
            {
                _base = null;
                return;
            }
            _base = Count();
        }

        /// <summary>What changed since the frame began (and start counting from here, for the next request).</summary>
        public static Dictionary<string, int> TakeChange()
        {
            var now = Count();
            if (now == null || _base == null)
            {
                _base = now;
                return null;
            }
            var change = new Dictionary<string, int>();
            foreach (var kv in now)
            {
                _base.TryGetValue(kv.Key, out int was);
                if (kv.Value != was)
                {
                    change[kv.Key] = kv.Value - was;
                }
            }
            foreach (var kv in _base)
            {
                if (!now.ContainsKey(kv.Key))
                {
                    change[kv.Key] = -kv.Value;
                }
            }
            _base = now;
            if (change.Count > 0)
            {
                return change;
            }
            return null;
        }

        /// <summary>Give back what was used up, take away what was gained.</summary>
        public static void Undo(Dictionary<string, int> change)
        {
            var p = Player.Instance;
            if (change == null || p == null)
            {
                return;
            }
            foreach (var kv in change)
            {
                try
                {
                    if (kv.Value < 0)
                    {
                        p.Inventory.addItemTypeToPlayer(kv.Key, -kv.Value, dropIfNoRoom: true);
                    }
                    else if (kv.Value > 0)
                    {
                        p.Inventory.removeItemAmountFromPlayer(kv.Key, kv.Value);
                    }
                    string text = "took back";
                    if (kv.Value < 0)
                    {
                        text = "gave back";
                    }
                    Plugin.Log.LogInfo($"[authority] {text} {Math.Abs(kv.Value)} {kv.Key}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[authority] couldn't undo {kv.Value} {kv.Key}: {e.Message}");
                }
            }
            _base = Count();
        }

        private static Dictionary<string, int> Count()
        {
            var p = Player.Instance;
            if (p == null || p.Inventory == null)
            {
                return null;
            }
            var d = new Dictionary<string, int>();
            void Add(Inventory inv)
            {
                if (inv?.slots == null)
                {
                    return;
                }
                foreach (var slot in inv.slots)
                {
                    InvItemClass it = null;
                    if (slot != null)
                    {
                        it = slot.invItem;
                    }
                    if (InvItemClass.isNull(it))
                    {
                        continue;
                    }
                    d.TryGetValue(it.type, out int n);
                    d[it.type] = n + Math.Max(1, it.amount);
                }
            }
            Add(p.Inventory);
            Add(p.Hotbar);
            return d;
        }
    }
}
