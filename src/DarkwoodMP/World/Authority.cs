using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The host is the authority for everything a client's player does to the
    /// shared world. The client does it at once in its own game, as a
    /// prediction, and sends it as a Request; the host checks it against its
    /// own world, makes it there (rolling any chance itself) and answers with a
    /// Verdict. A refused request is rolled back in the client's game.
    ///
    /// Request (client to host): u32 seq, then a whole message (its type byte
    /// and payload), which the host handles with that message's own handler.
    /// The handler may refuse it (Refuse), and may say what came of it (Result,
    /// for things decided on the host: the damage dealt, whether a disarm
    /// worked).
    ///
    /// Verdict (host to client): u32 seq, u8 ok, str reason (why it was
    /// refused), str result.
    ///
    /// Rolling back a refused request: before the Verdict, the host sends the
    /// state of what it was about as it really is (ObjState, from the handler);
    /// and the client undoes what its own inventory gained or lost in the frame
    /// it did it (items it used up come back, items it got go again). This is
    /// the client-side half of server reconciliation: the client predicts, the
    /// host decides, and a wrong prediction is replaced by the host's state.
    ///
    /// It isn't rollback in the fighting-game sense (re-simulating frames from
    /// the last confirmed input): that needs a deterministic game that can save
    /// and restore its whole state every frame, which Darkwood can't.
    ///
    /// The core decides (AuthorityRules, ReachRules) and holds the bytes
    /// (RequestMessage, VerdictMessage). One lives for each session, in
    /// CoopSession.Authority.
    /// </summary>
    internal sealed class Authority
    {
        private readonly CoopSession _coop;
        private readonly Session _session;

        /// <summary>
        /// Client: what our player carries, to undo a refused request's
        /// change.
        /// </summary>
        public readonly InventoryWatch Inventory = new InventoryWatch();

        // ---- client ---------------------------------------------------------

        private sealed class Pending
        {
            public string What;
            public float At;
            public Dictionary<string, int> Inventory;
            public Action<bool, string, string> Done;
        }

        private uint _seq;
        private readonly Dictionary<uint, Pending> _pending =
            new Dictionary<uint, Pending>();
        private readonly byte[] _copy =
            new byte[AuthorityRules.MaxRequest];

        /// <summary>Requests sent and not answered yet.</summary>
        public int Waiting => _pending.Count;
        /// <summary>
        /// Requests the host refused (and that were rolled back) this session.
        /// </summary>
        public int RolledBack { get; private set; }

        private readonly Func<ulong, MsgType, bool> _allowBare;

        public Authority(CoopSession coop)
        {
            _coop = coop;
            _session = coop.Net;
            coop.Handle(MsgType.Request, OnRequest);
            coop.Handle(MsgType.Verdict, OnVerdict);
            _allowBare = AllowBare;
            _session.AllowBare = _allowBare;
            coop.Hook(StopAllowBare);
            coop.Hook(EndPending);
        }

        /// <summary>
        /// The session is over: stop checking messages, unless the next
        /// session's Authority (made before this one ends) already does.
        /// </summary>
        private void StopAllowBare()
        {
            if (_session.AllowBare == _allowBare)
            {
                _session.AllowBare = null;
            }
        }

        /// <summary>
        /// The session ended with requests unanswered: tell each caller it was
        /// refused, so state kept until the Verdict (a button waiting for the
        /// host, a disarm in progress) is let go. The inventory isn't undone:
        /// the host's world is gone, and the caller's own rollback is all that
        /// runs.
        /// </summary>
        private void EndPending()
        {
            var left = new List<Pending>(_pending.Values);
            _pending.Clear();
            string ended =
                Refusals.Text(Verdict.Refuse(Refusal.SessionEnded));
            foreach (var p in left)
            {
                try
                {
                    Action<bool, string, string> handler = p.Done;
                    if (handler != null)
                    {
                        handler(false, ended, "");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[authority] after {p.What}: {e.Message}");
                }
            }
        }

        private readonly HashSet<(ulong, MsgType)> _bareWarned =
            new HashSet<(ulong, MsgType)>();

        /// <summary>
        /// Host (Session.Dispatch): whether a client's message may be handled
        /// as it came (AuthorityRules.TakesBare).
        /// </summary>
        private bool AllowBare(ulong from, MsgType type)
        {
            if (AuthorityRules.TakesBare(type, Handling))
            {
                return true;
            }

            if (_bareWarned.Add((from, type)))
            {
                string name = _session.NameOf(from);
                if (name == null)
                {
                    name = "#" + from;
                }

                Plugin.Log.LogWarning(
                    $"[authority] dropped {type} from {name}: it's only taken "
                        + "as a request");
            }

            return false;
        }

        /// <summary>
        /// Client: send a message the host must agree to. what: for the log.
        /// done(ok, reason, result) runs when the Verdict comes, after any
        /// rollback. The inventory change this frame goes with it, to undo if
        /// refused (unless inventory is false: for a request that isn't about
        /// items, made from a coroutine at any moment).
        /// </summary>
        public void Request(
            PacketWriter message, string what,
            Action<bool, string, string> done = null, bool inventory = true)
        {
            var s = _session;
            if (!s.IsClient)
            {
                return;
            }

            int n = message.Length;
            if (!AuthorityRules.Fits(n))
            {
                Plugin.Log.LogWarning($"Request too long: {what}");
                return;
            }

            Buffer.BlockCopy(message.Buffer, 0, _copy, 0, n);
            uint seq = ++_seq;
            var pending =
                new Pending { What = what, At = Time.realtimeSinceStartup };
            if (inventory)
            {
                pending.Inventory = Inventory.TakeChange();
            }

            pending.Done = done;
            _pending[seq] = pending;
            var w = s.Begin(MsgType.Request);
            RequestMessage.Write(w, seq, new ArraySegment<byte>(_copy, 0, n));
            s.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[authority] request {seq}: {what}");
            }
        }

        private void OnVerdict(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsClient || from != s.HostId)
            {
                return;
            }

            var reply = VerdictMessage.Read(r);
            if (!_pending.TryGetValue(reply.Seq, out var p))
            {
                return;
            }

            _pending.Remove(reply.Seq);
            if (!reply.Ok)
            {
                RolledBack++;
                Plugin.Log.LogInfo(
                    $"[authority] the host refused {p.What}: {reply.Reason}; "
                        + "rolled back");
                Inventory.Undo(p.Inventory);
            }
            else if (WorldActions.Debug)
            {
                string text = "";
                if (reply.Result.Length > 0)
                {
                    text = " (" + reply.Result + ")";
                }

                Plugin.Log.LogInfo(
                    $"[authority] {p.What}: ok{text} after "
                        + $"{(Time.realtimeSinceStartup - p.At) * 1000f:0} ms");
            }

            try
            {
                Action<bool, string, string> handler = p.Done;
                if (handler != null)
                {
                    handler(reply.Ok, reply.Reason, reply.Result);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[authority] after {p.What}: {e.Message}");
            }
        }

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// Host: what has been decided about the request being handled, or
        /// the last one handled (AuthorityRules).
        /// </summary>
        private RequestAnswer _answer;

        /// <summary>
        /// Host: true while handling a client's Request (its handler may Refuse
        /// it or set a Result).
        /// </summary>
        public bool Handling => _answer.Handling;
        /// <summary>Host: who sent the request being handled.</summary>
        public ulong From => _answer.From;

        /// <summary>
        /// Host, in a request's handler: refuse it. Send the client the state
        /// it should roll back to first. The first refusal counts.
        /// </summary>
        public void Refuse(Verdict refusal)
        {
            _answer = AuthorityRules.Refuse(_answer, refusal);
        }

        /// <summary>
        /// Refuse(Verdict) in words not named yet (Refusal.Words), for the
        /// handlers that don't call their rules yet. Null refuses nothing.
        /// </summary>
        public void Refuse(string reason)
        {
            if (reason != null)
            {
                Refuse(Verdict.Refuse(Refusal.Words, reason));
            }
        }

        public bool Refused => _answer.Verdict.Refused;
        /// <summary>
        /// Host, in a request's handler: what came of it, for the client
        /// (decided here: damage, a roll).
        /// </summary>
        public void Result(string result)
        {
            _answer = AuthorityRules.Result(_answer, result);
        }

        private void OnRequest(ulong from, PacketReader r)
        {
            var s = _session;
            if (!s.IsHost)
            {
                return;
            }

            byte[] inner = RequestMessage.Read(r, out uint seq);
            _answer = AuthorityRules.Begin(from);
            try
            {
                bool nested = inner.Length > 0
                    && (MsgType)inner[0] == MsgType.Request;
                var may = AuthorityRules.MayHandle(inner.Length, nested);
                if (may.Refused)
                {
                    Refuse(may);
                }
                else
                {
                    s.Handle(from, inner, inner.Length);
                }
            }
            catch (Exception e)
            {
                Refuse(Verdict.Refuse(Refusal.HandlerFailed, e.Message));
            }
            finally
            {
                _answer = AuthorityRules.End(_answer);
            }

            var w = s.Begin(MsgType.Verdict);
            VerdictMessage.Write(w, seq, _answer);
            s.SendTo(from, w, Channel.Control, reliable: true);
            if (_answer.Verdict.Refused)
            {
                string what = "nothing";
                if (inner.Length > 0)
                {
                    what = ((MsgType)inner[0]).ToString();
                }

                Plugin.Log.LogInfo(
                    $"[authority] refused {what} from {s.NameOf(from)}: "
                        + $"{Refusals.Text(_answer.Verdict)}");
            }
        }

        /// <summary>
        /// Host: the proxy (the host's stand-in) of the player whose request is
        /// being handled.
        /// </summary>
        public NetPlayerProxy Requester
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
        /// Host, in a request's handler: the requesting player is somewhere the
        /// host's game doesn't run (their stand-in is Away: a dream of their
        /// own, a separate location the host hasn't got at their spot). What
        /// they did there isn't in the host's world, so it can't be checked
        /// here, nor refused.
        /// </summary>
        public bool RequesterAway
        {
            get
            {
                var p = Requester;
                bool known = p != null;
                bool away = known && p.Away;
                return AuthorityRules.RequesterAway(Handling, known, away);
            }
        }

        /// <summary>
        /// Host, in a request's handler: take back a refusal made while
        /// checking it, because the request is about a place only the requester
        /// has (RequesterAway). It is accepted and not applied here; result
        /// tells the requester so (ScriptedEvents: "alone" runs the event's
        /// every part there).
        /// </summary>
        public void AcceptAway(string what, string result = "")
        {
            if (!Handling)
            {
                return;
            }

            string refusal = "not here";
            if (Refused)
            {
                refusal = Refusals.Text(_answer.Verdict);
            }

            Plugin.Log.LogInfo(
                $"[authority] {_session.NameOf(From)}'s {what}: they're "
                    + "where the "
                    + $"host doesn't run ({refusal}), so it's theirs alone: "
                    + "accepted, not applied here");
            _answer = AuthorityRules.AcceptAway(_answer, result);
        }

        /// <summary>
        /// Host: whether this player's stand-in is within reach of pos (true if
        /// it isn't known yet).
        /// </summary>
        public bool Near(ulong player, Vector3 pos, float reach)
        {
            NetPlayerProxy.All.TryGetValue(player, out var p);
            var facts = FactsOf(p, pos) with { Reach = reach };
            return ReachRules.Near(facts);
        }

        /// <summary>
        /// Host, in a request's handler: refuse it if the requesting player is
        /// further than reach from pos. The stand-in follows the puppet, which
        /// moves on the next frame; the player's newest position (a
        /// PlayerState flushed just before this request,
        /// Sync.PlayerSync.FlushNow) has already been through MoveCheck, so
        /// it's as trustworthy as the stand-in and newer: the nearer counts.
        /// </summary>
        public bool InReach(Vector3 pos, float reach, string what)
        {
            var facts = ReachOf(pos) with { Reach = reach, What = what };
            var verdict = ReachRules.InReach(facts);
            Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// Host, in a request's handler: what the reach rules know of the
        /// requesting player and pos, for a subsystem's facts. The rule sets
        /// the reach and the name.
        /// </summary>
        public ReachFacts ReachOf(Vector3 pos)
        {
            var facts = FactsOf(Requester, pos) with { Handling = Handling };
            if (_coop.Moves.TryAccepted(From, out var at))
            {
                facts = facts with { Checked = true, Accepted = Flat(at) };
            }

            return facts;
        }

        /// <summary>
        /// What the reach rules know of a player's stand-in and a target: the
        /// only code that reads the stand-in for them. p may be null (or
        /// destroyed): no stand-in yet.
        /// </summary>
        private static ReachFacts FactsOf(NetPlayerProxy p, Vector3 target)
        {
            var facts = new ReachFacts { Target = Flat(target) };
            if (p != null)
            {
                facts = facts with
                {
                    Known = true,
                    StandIn = Flat(p.transform.position),
                };
            }

            return facts;
        }

        private static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);
    }
}
