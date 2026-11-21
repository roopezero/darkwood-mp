using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DarkwoodMP.Net.Steam;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Thin wrapper over ISteamNetworkingMessages: connectionless,
    /// Steam-relayed P2P (no port forwarding, NAT traversal handled by Steam
    /// Datagram Relay).
    /// </summary>
    public sealed class SteamTransport : IDisposable
    {
        public delegate void MessageHandler(
            CSteamID from, byte[] data, int length, int channel);

        public event MessageHandler OnMessage;

        /// <summary>
        /// Whether to accept a P2P session request from this user
        /// (SteamPeerRules.MayAccept).
        /// </summary>
        public Func<CSteamID, Verdict> ShouldAccept =
            _ => Verdict.Refuse(Refusal.NotInLobby);

        private Callback<SteamNetworkingMessagesSessionRequest_t>
            _sessionRequest;
        private Callback<SteamNetworkingMessagesSessionFailed_t> _sessionFailed;
        private readonly IntPtr[] _msgPtrs = new IntPtr[64];
        private byte[] _recvBuf = new byte[4096];

        public void Init()
        {
            _sessionRequest =
                Callback<SteamNetworkingMessagesSessionRequest_t>.Create(
                    OnSessionRequest);
            _sessionFailed =
                Callback<SteamNetworkingMessagesSessionFailed_t>.Create(
                    OnSessionFailed);
            // Warm up the relay network so the first connection is fast.
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        private void OnSessionRequest(
            SteamNetworkingMessagesSessionRequest_t req)
        {
            var id = req.m_identityRemote.GetSteamID();
            Verdict why = ShouldAccept(id);
            if (!why.Refused)
            {
                var ident = req.m_identityRemote;
                SteamNetworkingMessages.AcceptSessionWithUser(ref ident);
                Plugin.Log.LogInfo($"Accepted P2P session from {id}");
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"Rejected P2P session from {id} "
                        + $"({Refusals.Text(why)})");
            }
        }

        private void OnSessionFailed(
            SteamNetworkingMessagesSessionFailed_t fail)
        {
            Plugin.Log.LogWarning(
                $"P2P session failed: {fail.m_info.m_eEndReason} "
                    + $"{fail.m_info.m_szEndDebug}");
            Action<CSteamID> handler = OnSessionLost;
            if (handler != null)
            {
                handler(fail.m_info.m_identityRemote.GetSteamID());
            }
        }

        /// <summary>
        /// Steam gave up on the connection with this user (it timed out, or its
        /// end closed). Steam's own keepalives run outside the game's main
        /// thread, so this also works while either game is busy loading.
        /// </summary>
        public event Action<CSteamID> OnSessionLost;

        // Reliable messages Steam had no room for, per peer, in the order they
        // were sent. Steam queues only about 512 KiB per peer (its
        // SendBufferSize) and refuses more with k_EResultLimitExceeded; what it
        // refuses waits here and goes out as room frees up.
        private sealed class Pending
        {
            public byte[] Data;
            public int Channel;
        }

        private readonly Dictionary<CSteamID, Queue<Pending>> _pending =
            new Dictionary<CSteamID, Queue<Pending>>();
        private readonly Dictionary<CSteamID, long> _pendingBytes =
            new Dictionary<CSteamID, long>();

        // Steam refuses any one message over 512 KiB
        // (k_cbMaxSteamNetworkingSocketsMessageSizeSend) with
        // k_EResultInvalidParam; TCP takes up to Protocol.MaxMessage. A longer
        // reliable message goes as SteamPiece messages (SteamPieces), and is
        // joined again on the other side before it's handled.
        public bool Send(
            CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            if (SteamSendRules.TooLongForOne(length))
            {
                if (reliable)
                {
                    return SendPieces(to, data, length, channel);
                }

                var why = SteamSendRules.TooLong(
                    length,
                    ((MsgType)data[0]).ToString());
                Plugin.Log.LogWarning(
                    $"Send to {to} failed: {Refusals.Text(why)}");
                return false;
            }

            return SendOne(to, data, length, channel, reliable);
        }

        private bool SendPieces(
            CSteamID to, byte[] data, int length, int channel)
        {
            bool ok = true;
            // Every piece is tried even after one fails, so the other side
            // never joins one message's start to another's end.
            var message = new ArraySegment<byte>(data, 0, length);
            var pieces = SteamPieces.Split(
                message,
                (byte)MsgType.SteamPiece);
            foreach (var piece in pieces)
            {
                ok &= SendOne(to, piece, piece.Length, channel, reliable: true);
            }

            return ok;
        }

        // The long messages coming in as SteamPiece messages, per sender and
        // channel.
        private readonly PieceJoiner _joiner =
            new PieceJoiner(Protocol.MaxMessage);

        private bool SendOne(
            CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            // Reliable messages keep their order: once one waits, the ones
            // after it wait behind it.
            if (reliable && _pending.TryGetValue(to, out var q)
                && SteamSendRules.WaitsBehind(q.Count))
            {
                return Enqueue(to, data, length, channel);
            }

            var res = SendNow(to, data, length, channel, reliable);
            if (!reliable)
            {
                return SteamSendRules.AfterUnreliable(ResultOf(res));
            }

            var step = SteamSendRules.AfterReliable(ResultOf(res));
            if (step == SendStep.Queue)
            {
                return Enqueue(to, data, length, channel);
            }

            if (step == SendStep.Failed)
            {
                Plugin.Log.LogWarning($"Send to {to} failed: {res}");
                return false;
            }

            return true;
        }

        /// <summary>Steam's answer to a send, for SteamSendRules.</summary>
        private static SendResult ResultOf(EResult res)
        {
            if (res == EResult.k_EResultOK)
            {
                return SendResult.Sent;
            }

            if (res == EResult.k_EResultLimitExceeded)
            {
                return SendResult.Full;
            }

            return SendResult.Failed;
        }

        private EResult SendNow(
            CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            var ident = new SteamNetworkingIdentity();
            ident.SetSteamID(to);
            int mode;
            if (reliable)
            {
                mode = Constants.k_nSteamNetworkingSend_Reliable;
            }
            else
            {
                mode = Constants.k_nSteamNetworkingSend_UnreliableNoDelay;
            }

            int flags = mode
                | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return SteamNetworkingMessages.SendMessageToUser(
                    ref ident, h.AddrOfPinnedObject(), (uint)length, flags,
                    channel);
            }
            finally
            {
                h.Free();
            }
        }

        private bool Enqueue(CSteamID to, byte[] data, int length, int channel)
        {
            if (!_pending.TryGetValue(to, out var q))
            {
                _pending[to] = q = new Queue<Pending>();
            }

            _pendingBytes.TryGetValue(to, out long bytes);
            var why = SteamSendRules.MayQueue(bytes, length);
            if (why.Refused)
            {
                Plugin.Log.LogWarning(
                    $"Send to {to} failed: {Refusals.Text(why)}");
                return false;
            }

            var copy = new byte[length];
            // the caller's buffer is reused for the next message
            Buffer.BlockCopy(data, 0, copy, 0, length);
            q.Enqueue(new Pending { Data = copy, Channel = channel });
            _pendingBytes[to] = bytes + length;
            return true;
        }

        /// <summary>
        /// Forget everything still waiting (we left the lobby).
        /// </summary>
        public void ClearPending()
        {
            _pending.Clear();
            _pendingBytes.Clear();
            _joiner.Clear();
            _closing.Clear();
        }

        // Peers to close once what was sent to them has gone out (a refused
        // joiner must get its Refused first). The value is when to close: -1
        // while messages still wait in _pending, then CloseDelay seconds after
        // they all went to Steam, which gives Steam time to deliver its own
        // queue before the session closes (SteamSendRules.Close).
        private readonly Dictionary<CSteamID, float> _closing =
            new Dictionary<CSteamID, float>();

        /// <summary>
        /// Close the session with this peer once what was sent to it has gone
        /// out.
        /// </summary>
        public void CloseWhenSent(CSteamID peer)
        {
            if (!_closing.ContainsKey(peer))
            {
                _closing[peer] = -1f;
            }
        }

        private void CloseSent()
        {
            if (_closing.Count == 0)
            {
                return;
            }

            var now = new Seconds(UnityEngine.Time.unscaledTime);
            foreach (var id in new List<CSteamID>(_closing.Keys))
            {
                var step = SteamSendRules.Close(CloseOf(id, now));
                if (step == CloseStep.Arm)
                {
                    _closing[id] = SteamSendRules.CloseAt(now).Value;
                }
                else if (step == CloseStep.Close)
                {
                    CloseSession(id);
                }
            }
        }

        /// <summary>A peer waiting to be closed, for SteamSendRules.</summary>
        private CloseFacts CloseOf(CSteamID id, Seconds now)
        {
            float at = _closing[id];
            return new CloseFacts
            {
                Waiting = _pending.ContainsKey(id),
                Armed = at >= 0f,
                At = new Seconds(at),
                Now = now,
            };
        }

        /// <summary>
        /// Send what waited for room in Steam's queue, as far as there's room
        /// now.
        /// </summary>
        private void FlushPending()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            List<CSteamID> done = null;
            foreach (var kv in _pending)
            {
                var q = kv.Value;
                while (q.Count > 0)
                {
                    var m = q.Peek();
                    var res = SendNow(
                        kv.Key, m.Data, m.Data.Length, m.Channel,
                        reliable: true);
                    var step = SteamSendRules.AfterFlush(ResultOf(res));
                    if (step == FlushStep.Stop)
                    {
                        break; // still full: next frame
                    }

                    if (step == FlushStep.Drop)
                    {
                        Plugin.Log.LogWarning(
                            $"Send to {kv.Key} failed: {res}");
                    }

                    q.Dequeue();
                    _pendingBytes[kv.Key] -= m.Data.Length;
                }

                if (q.Count == 0)
                {
                    if (done == null)
                    {
                        done = new List<CSteamID>();
                    }

                    done.Add(kv.Key);
                }
            }

            if (done != null)
            {
                foreach (var id in done)
                {
                    _pending.Remove(id);
                    _pendingBytes.Remove(id);
                }
            }
        }

        /// <summary>
        /// Drain incoming messages on all channels. Call once per frame.
        /// </summary>
        public void Poll()
        {
            FlushPending();
            CloseSent();
            PollChannel(Channel.Control);
            PollChannel(Channel.State);
        }

        private void PollChannel(int channel)
        {
            while (true)
            {
                int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(
                    channel, _msgPtrs, _msgPtrs.Length);
                if (n <= 0)
                {
                    return;
                }

                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var msg =
                            Marshal.PtrToStructure<SteamNetworkingMessage_t>(
                                _msgPtrs[i]);
                        int len = msg.m_cbSize;
                        if (len > 0)
                        {
                            if (_recvBuf.Length < len)
                            {
                                _recvBuf = new byte[Math.Max(
                                    len, _recvBuf.Length * 2)];
                            }

                            Marshal.Copy(msg.m_pData, _recvBuf, 0, len);
                            var from = msg.m_identityPeer.GetSteamID();
                            if (_recvBuf[0] != (byte)MsgType.SteamPiece)
                            {
                                MessageHandler handler = OnMessage;
                                if (handler != null)
                                {
                                    handler(from, _recvBuf, len, channel);
                                }
                            }
                            else
                            {
                                AddPiece(from, channel, len);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"Error handling packet: {e}");
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(_msgPtrs[i]);
                    }
                }

                if (n < _msgPtrs.Length)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Add a SteamPiece from _recvBuf; hand on the whole message once its
        /// last piece is in.
        /// </summary>
        private void AddPiece(CSteamID from, int channel, int len)
        {
            var piece = new ArraySegment<byte>(_recvBuf, 0, len);
            var answer = _joiner.Add(from.m_SteamID, channel, piece);
            if (answer.Why.Refused)
            {
                string what = "a piece";
                if (answer.Why.Reason == Refusal.PiecesTooLong)
                {
                    what = "a long message";
                }

                Plugin.Log.LogWarning(
                    $"Dropped {what} from {from}: "
                        + Refusals.Text(answer.Why));
                return;
            }

            var whole = answer.Whole;
            if (whole == null)
            {
                return;
            }

            MessageHandler receiver = OnMessage;
            if (receiver != null)
            {
                receiver(from, whole, whole.Length, channel);
            }
        }

        public void CloseSession(CSteamID peer)
        {
            _closing.Remove(peer);
            _pending.Remove(peer);
            _pendingBytes.Remove(peer);
            _joiner.Forget(peer.m_SteamID, Channel.Control);
            _joiner.Forget(peer.m_SteamID, Channel.State);
            var ident = new SteamNetworkingIdentity();
            ident.SetSteamID(peer);
            SteamNetworkingMessages.CloseSessionWithUser(ref ident);
        }

        public void Dispose()
        {
            Callback<SteamNetworkingMessagesSessionRequest_t> sessionRequest =
                _sessionRequest;
            if (sessionRequest != null)
            {
                sessionRequest.Dispose();
            }

            Callback<SteamNetworkingMessagesSessionFailed_t> sessionFailed =
                _sessionFailed;
            if (sessionFailed != null)
            {
                sessionFailed.Dispose();
            }
        }
    }
}
