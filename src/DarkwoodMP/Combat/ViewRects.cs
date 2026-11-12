using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// How much of the world each player's game keeps running round them: the game's cull rect, which comes from
    /// the screen's size and the camera's zoom (WorldGrid.refreshNodes). A joiner sends its own (ViewRect) when it's
    /// in the world and whenever it changes, and the host keeps that much entered round them (SharedCullingPatch).
    /// Until it's known, the host uses its own.
    /// </summary>
    public static class ViewRects
    {
        private static readonly Dictionary<ulong, (int dx, int dy)> Rects = new Dictionary<ulong, (int, int)>();
        private static (int, int) _sent;
        private static float _nextCheck;

        public static void Register(Session s)
        {
            s.Register(MsgType.ViewRect, OnRect);
            s.OnPeerDisconnected += id => Rects.Remove(id);
            s.OnSessionEnded += () => { Rects.Clear(); _sent = default; };
        }

        /// <summary>Client, every frame: send our cull rect when it's new or changed (checked every 2 s).</summary>
        public static void Update()
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || !World.WorldShare.InWorld || Singleton<Controller>.Instance == null) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;
            SharedCullingPatch.Distances(out int dx, out int dy);
            if (dx <= 0 || dy <= 0 || (dx, dy) == _sent) return;
            _sent = (dx, dy);
            var w = s.Begin(MsgType.ViewRect);
            w.W.Write((ushort)Mathf.Clamp(dx, 0, ushort.MaxValue));
            w.W.Write((ushort)Mathf.Clamp(dy, 0, ushort.MaxValue));
            s.SendToHost(w, Channel.Control, reliable: true);
        }

        private static void OnRect(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost) return;
            // A rect no screen gives is kept within bounds: a huge one would keep the whole map running.
            int dx = Mathf.Clamp(r.R.ReadUInt16(), 600, 6000), dy = Mathf.Clamp(r.R.ReadUInt16(), 600, 6000);
            if (Rects.TryGetValue(from, out var was) && was == (dx, dy)) return;
            Rects[from] = (dx, dy);
            Plugin.Log.LogInfo($"{s.NameOf(from)}'s game keeps {dx} x {dy} round them running (half width and height): so does ours");
        }

        /// <summary>Host: the half width and height to keep entered round this player.</summary>
        public static void For(ulong peer, out int dx, out int dy)
        {
            if (Rects.TryGetValue(peer, out var rc)) { dx = rc.dx; dy = rc.dy; }
            else SharedCullingPatch.Distances(out dx, out dy);
        }
    }
}
