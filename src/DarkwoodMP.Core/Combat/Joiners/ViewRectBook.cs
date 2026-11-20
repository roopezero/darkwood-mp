using System.Collections.Generic;

namespace DarkwoodMP.Combat.Joiners
{
    /// The host's view rect for each client, by peer id.
    public sealed class ViewRectBook
    {
        readonly Dictionary<ulong, ViewRect> _rects =
            new Dictionary<ulong, ViewRect>();

        /// Keeps a client's reported rect, clamped
        /// (ViewRectRules.Accepted). False when it is the one already
        /// kept: nothing to log.
        public bool Accept(ulong peer, ViewRect reported)
        {
            var rect = ViewRectRules.Accepted(reported);
            if (_rects.TryGetValue(peer, out var was) && was.Equals(rect))
            {
                return false;
            }

            _rects[peer] = rect;
            return true;
        }

        /// The rect kept for a client; false with none, and the shell uses
        /// the host's own (SharedCullingPatch.Distances).
        public bool TryGet(ulong peer, out ViewRect rect) =>
            _rects.TryGetValue(peer, out rect);

        /// A client left.
        public void Forget(ulong peer)
        {
            _rects.Remove(peer);
        }

        /// The session ended.
        public void Clear()
        {
            _rects.Clear();
        }
    }
}
