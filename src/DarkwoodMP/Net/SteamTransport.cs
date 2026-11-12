using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Thin wrapper over ISteamNetworkingMessages: connectionless, Steam-relayed P2P
    /// (no port forwarding, NAT traversal handled by Steam Datagram Relay).
    /// </summary>
    public sealed class SteamTransport : IDisposable
    {
        public delegate void MessageHandler(CSteamID from, byte[] data, int length, int channel);

        public event MessageHandler OnMessage;

        /// <summary>Return true to accept a P2P session request from this user.</summary>
        public Func<CSteamID, bool> ShouldAccept = _ => false;

        private Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;
        private Callback<SteamNetworkingMessagesSessionFailed_t> _sessionFailed;
        private readonly IntPtr[] _msgPtrs = new IntPtr[64];
        private byte[] _recvBuf = new byte[4096];

        public void Init()
        {
            _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            _sessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
            // Warm up the relay network so the first connection is fast.
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t req)
        {
            var id = req.m_identityRemote.GetSteamID();
            if (ShouldAccept(id))
            {
                var ident = req.m_identityRemote;
                SteamNetworkingMessages.AcceptSessionWithUser(ref ident);
                Plugin.Log.LogInfo($"Accepted P2P session from {id}");
            }
            else
            {
                Plugin.Log.LogWarning($"Rejected P2P session from {id} (not in lobby)");
            }
        }

        private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t fail)
        {
            Plugin.Log.LogWarning($"P2P session failed: {fail.m_info.m_eEndReason} {fail.m_info.m_szEndDebug}");
            OnSessionLost?.Invoke(fail.m_info.m_identityRemote.GetSteamID());
        }

        /// <summary>
        /// Steam gave up on the connection with this user (it timed out, or its end closed). Steam's own keepalives
        /// run outside the game's main thread, so this also works while either game is busy loading.
        /// </summary>
        public event Action<CSteamID> OnSessionLost;

        // Reliable messages Steam had no room for, per peer, in the order they were sent. Steam queues only about
        // 512 KiB per peer (its SendBufferSize) and refuses more with k_EResultLimitExceeded; a joiner asks for
        // the host's world 1 MiB at a time, so pieces of it were lost and the download stopped short.
        private sealed class Pending { public byte[] Data; public int Channel; }
        private readonly System.Collections.Generic.Dictionary<CSteamID, System.Collections.Generic.Queue<Pending>> _pending =
            new System.Collections.Generic.Dictionary<CSteamID, System.Collections.Generic.Queue<Pending>>();
        private readonly System.Collections.Generic.Dictionary<CSteamID, long> _pendingBytes = new System.Collections.Generic.Dictionary<CSteamID, long>();
        private const long MaxPendingBytes = 64L << 20; // past this the peer isn't taking anything: give up on it

        // Steam refuses any one message over 512 KiB (k_cbMaxSteamNetworkingSocketsMessageSizeSend) with
        // k_EResultInvalidParam; TCP takes up to Protocol.MaxMessage. A longer reliable message goes as SteamPiece
        // messages of PieceSize bytes each, and is joined again on the other side before it's handled.
        private const int PieceSize = 256 << 10;
        private const int MaxSteamMessage = 512 << 10;

        public bool Send(CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            if (length > MaxSteamMessage)
            {
                if (reliable) return SendPieces(to, data, length, channel);
                Plugin.Log.LogWarning($"Send to {to} failed: an unreliable {(MsgType)data[0]} of {length / 1024} KiB is too long for Steam");
                return false;
            }
            return SendOne(to, data, length, channel, reliable);
        }

        private bool SendPieces(CSteamID to, byte[] data, int length, int channel)
        {
            bool ok = true;
            // Every piece is tried even after one fails, so the other side never joins one message's start to another's end.
            for (int off = 0; off < length; off += PieceSize)
            {
                int n = Math.Min(PieceSize, length - off);
                int head = off == 0 ? 5 : 1;
                var piece = new byte[head + n];
                piece[0] = (byte)MsgType.SteamPiece;
                if (off == 0) { piece[1] = (byte)length; piece[2] = (byte)(length >> 8); piece[3] = (byte)(length >> 16); piece[4] = (byte)(length >> 24); }
                Buffer.BlockCopy(data, off, piece, head, n);
                ok &= SendOne(to, piece, piece.Length, channel, reliable: true);
            }
            return ok;
        }

        // A long message coming in as SteamPiece messages, per sender and channel.
        private sealed class Joining { public byte[] Data; public int Got; }
        private readonly System.Collections.Generic.Dictionary<(CSteamID, int), Joining> _joining =
            new System.Collections.Generic.Dictionary<(CSteamID, int), Joining>();

        /// <summary>Add a SteamPiece; returns the whole message once its last piece is in, else null.</summary>
        private Joining AddPiece(CSteamID from, int channel, byte[] buf, int len)
        {
            var key = (from, channel);
            if (!_joining.TryGetValue(key, out var j))
            {
                int total = len >= 5 ? buf[1] | buf[2] << 8 | buf[3] << 16 | buf[4] << 24 : -1;
                if (total <= 0 || total > Protocol.MaxMessage)
                {
                    Plugin.Log.LogWarning($"Dropped a piece from {from}: a message of {total} bytes");
                    return null;
                }
                _joining[key] = j = new Joining { Data = new byte[total] };
                return Append(key, j, buf, 5, len - 5);
            }
            return Append(key, j, buf, 1, len - 1);
        }

        private Joining Append((CSteamID, int) key, Joining j, byte[] buf, int at, int n)
        {
            if (j.Got + n > j.Data.Length)
            {
                Plugin.Log.LogWarning($"Dropped a long message from {key.Item1}: its pieces ran past its length");
                _joining.Remove(key);
                return null;
            }
            Buffer.BlockCopy(buf, at, j.Data, j.Got, n);
            j.Got += n;
            if (j.Got < j.Data.Length) return null;
            _joining.Remove(key);
            return j;
        }

        private bool SendOne(CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            // Reliable messages keep their order: once one waits, the ones after it wait behind it.
            if (reliable && _pending.TryGetValue(to, out var q) && q.Count > 0) return Enqueue(to, data, length, channel);
            var res = SendNow(to, data, length, channel, reliable);
            if (res == EResult.k_EResultLimitExceeded && reliable) return Enqueue(to, data, length, channel);
            if (res != EResult.k_EResultOK && reliable)
                Plugin.Log.LogWarning($"Send to {to} failed: {res}");
            return res == EResult.k_EResultOK;
        }

        private EResult SendNow(CSteamID to, byte[] data, int length, int channel, bool reliable)
        {
            var ident = new SteamNetworkingIdentity();
            ident.SetSteamID(to);
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return SteamNetworkingMessages.SendMessageToUser(ref ident, h.AddrOfPinnedObject(), (uint)length, flags, channel); }
            finally { h.Free(); }
        }

        private bool Enqueue(CSteamID to, byte[] data, int length, int channel)
        {
            if (!_pending.TryGetValue(to, out var q)) _pending[to] = q = new System.Collections.Generic.Queue<Pending>();
            _pendingBytes.TryGetValue(to, out long bytes);
            if (bytes + length > MaxPendingBytes)
            {
                Plugin.Log.LogWarning($"Send to {to} failed: {bytes / 1024} KiB already waiting for room");
                return false;
            }
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length); // the caller's buffer is reused for the next message
            q.Enqueue(new Pending { Data = copy, Channel = channel });
            _pendingBytes[to] = bytes + length;
            return true;
        }

        /// <summary>Forget everything still waiting (we left the lobby).</summary>
        public void ClearPending() { _pending.Clear(); _pendingBytes.Clear(); _joining.Clear(); }

        /// <summary>Send what waited for room in Steam's queue, as far as there's room now.</summary>
        private void FlushPending()
        {
            if (_pending.Count == 0) return;
            System.Collections.Generic.List<CSteamID> done = null;
            foreach (var kv in _pending)
            {
                var q = kv.Value;
                while (q.Count > 0)
                {
                    var m = q.Peek();
                    var res = SendNow(kv.Key, m.Data, m.Data.Length, m.Channel, reliable: true);
                    if (res == EResult.k_EResultLimitExceeded) break; // still full: next frame
                    if (res != EResult.k_EResultOK) Plugin.Log.LogWarning($"Send to {kv.Key} failed: {res}");
                    q.Dequeue();
                    _pendingBytes[kv.Key] -= m.Data.Length;
                }
                if (q.Count == 0) (done ??= new System.Collections.Generic.List<CSteamID>()).Add(kv.Key);
            }
            if (done != null) foreach (var id in done) { _pending.Remove(id); _pendingBytes.Remove(id); }
        }

        public bool Send(CSteamID to, PacketWriter pw, int channel, bool reliable) =>
            Send(to, pw.Buffer, pw.Length, channel, reliable);

        /// <summary>Drain incoming messages on all channels. Call once per frame.</summary>
        public void Poll()
        {
            FlushPending();
            PollChannel(Channel.Control);
            PollChannel(Channel.State);
        }

        private void PollChannel(int channel)
        {
            while (true)
            {
                int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _msgPtrs, _msgPtrs.Length);
                if (n <= 0) return;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_msgPtrs[i]);
                        int len = msg.m_cbSize;
                        if (len > 0)
                        {
                            if (_recvBuf.Length < len) _recvBuf = new byte[Math.Max(len, _recvBuf.Length * 2)];
                            Marshal.Copy(msg.m_pData, _recvBuf, 0, len);
                            var from = msg.m_identityPeer.GetSteamID();
                            if (_recvBuf[0] != (byte)MsgType.SteamPiece) OnMessage?.Invoke(from, _recvBuf, len, channel);
                            else if (AddPiece(from, channel, _recvBuf, len) is Joining whole) OnMessage?.Invoke(from, whole.Data, whole.Data.Length, channel);
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
                if (n < _msgPtrs.Length) return;
            }
        }

        public void CloseSession(CSteamID peer)
        {
            _pending.Remove(peer);
            _pendingBytes.Remove(peer);
            _joining.Remove((peer, Channel.Control));
            _joining.Remove((peer, Channel.State));
            var ident = new SteamNetworkingIdentity();
            ident.SetSteamID(peer);
            SteamNetworkingMessages.CloseSessionWithUser(ref ident);
        }

        public void Dispose()
        {
            _sessionRequest?.Dispose();
            _sessionFailed?.Dispose();
        }
    }
}
