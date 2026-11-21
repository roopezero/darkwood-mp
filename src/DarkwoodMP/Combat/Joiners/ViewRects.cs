using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// How much of the world each player's game keeps running round them: the
    /// game's cull rect, which comes from the screen's size and the camera's
    /// zoom (WorldGrid.refreshNodes). A joiner sends its own (ViewRect) when
    /// it's in the world and whenever it changes, and the host keeps that much
    /// entered round them (SharedCulling). Until it's known, the host uses
    /// its own. One per session (CoopSession.Views).
    /// </summary>
    internal sealed class ViewRects
    {
        private readonly CoopSession _coop;
        private readonly ViewRectBook _rects = new ViewRectBook();
        private readonly ViewRectSender _sender = new ViewRectSender();

        public ViewRects(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.ViewRect, OnRect);
            coop.Net.OnPeerDisconnected += Forget;
            coop.Hook(() => coop.Net.OnPeerDisconnected -= Forget);
        }

        /// <summary>
        /// Client, every frame: send our cull rect when it's new or changed
        /// (checked every 2 s).
        /// </summary>
        public void Update()
        {
            if (!_sender.Due(FactsOf(_coop.Net)))
            {
                return;
            }

            World.Culling.SharedCulling.Distances(out int dx, out int dy);
            var rect = new ViewRect(dx, dy);
            if (!_sender.Sends(rect))
            {
                return;
            }

            var w = _coop.Net.Begin(MsgType.ViewRect);
            ViewRectMessage.Write(w, rect);
            _coop.Net.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// Host: the half width and height to keep entered round this player;
        /// the host's own with no session or none known.
        /// </summary>
        public static void For(ulong peer, out int dx, out int dy)
        {
            CoopSession coop = Coop.Current;
            if (coop != null
                && coop.Views._rects.TryGet(peer, out ViewRect rect))
            {
                dx = rect.Dx;
                dy = rect.Dy;
            }
            else
            {
                World.Culling.SharedCulling.Distances(out dx, out dy);
            }
        }

        private static RectSendFacts FactsOf(Session s)
        {
            return new RectSendFacts
            {
                Client = s.IsClient,
                InWorld = World.WorldShare.InWorld,
                Controller = Singleton<Controller>.Instance != null,
                Now = new Seconds(Time.unscaledTime),
            };
        }

        private void Forget(ulong peer)
        {
            _rects.Forget(peer);
        }

        private void OnRect(ulong from, PacketReader r)
        {
            Session s = _coop.Net;
            if (!s.IsHost)
            {
                return;
            }

            // A rect no screen gives is kept within bounds: a huge one would
            // keep the whole map running.
            ViewRect reported = ViewRectMessage.Read(r);
            if (!_rects.Accept(from, reported))
            {
                return;
            }

            _rects.TryGet(from, out ViewRect rect);
            Plugin.Log.LogInfo(
                $"{s.NameOf(from)}'s game keeps {rect.Dx} x {rect.Dy} round "
                    + "them running (half width and height): so does ours");
        }
    }
}
