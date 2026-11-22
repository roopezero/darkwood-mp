using System;
using System.Collections.Generic;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The hammer stroke being made (Player.checkFrameTrigger "HammerWork"),
    /// and a client's actions held until it is over: the stroke that nails
    /// up a barricade or rebuilds a broken door calls Door.barricade (or
    /// Window.barricade) first and takes the wood and nails after it, so the
    /// request is sent once the call is over and carries the materials it
    /// used up (InventoryWatch gives them back if it is refused). One lives
    /// for each session, in CoopSession.Hammer.
    /// </summary>
    internal sealed class HammerStrokes
    {
        private readonly List<Action> _held = new List<Action>();
        private int _depth;

        /// <summary>Whether a stroke is being made now.</summary>
        public bool Striking => _depth > 0;

        /// <summary>The patch's prefix: a stroke starts.</summary>
        public void Enter()
        {
            _depth++;
        }

        /// <summary>
        /// Sends send once the stroke is over (WorldActions.Send).
        /// </summary>
        public void Hold(Action send)
        {
            _held.Add(send);
        }

        /// <summary>
        /// The patch's finalizer: a stroke is over, and once the outermost
        /// one is, what it held is sent.
        /// </summary>
        public void Leave()
        {
            if (--_depth > 0)
            {
                return;
            }

            _depth = 0;
            if (_held.Count == 0)
            {
                return;
            }

            var held = _held.ToArray();
            _held.Clear();
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
}
