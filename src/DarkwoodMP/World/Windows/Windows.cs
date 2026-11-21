using DarkwoodMP.Net;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World.Windows
{
    /// <summary>
    /// Windows, shared: when one is barricaded or its barricade is damaged
    /// or broken in a player's game, the same call is made in everyone
    /// else's (WorldActions sends and reads the WorldAction).
    /// </summary>
    internal sealed class Windows
    {
        private readonly CoopSession _coop;

        public Windows(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// Host: whether a client's player could have done this to the
        /// window, as this world is now. Refuses the request if not.
        /// </summary>
        internal bool Allowed(Action a, ObjRef obj, float x)
        {
            var window = ObjRef.Resolve<Window>(obj);
            if (window == null)
            {
                _coop.Authority.Refuse("no such window here");
                return false;
            }

            float windowReach;
            if (a == Action.WindowHit)
            {
                windowReach = WorldActions.ShotReach;
            }
            else
            {
                windowReach = WorldActions.Reach;
            }

            if (!_coop.Authority.InReach(
                window.transform.position, windowReach, window.name))
            {
                return false;
            }

            if (a == Action.WindowBarricade && window.barricaded)
            {
                _coop.Authority.Refuse(
                    "the window is already barricaded");
                return false;
            }

            if (a == Action.WindowDestroyBarricade && !window.barricaded)
            {
                _coop.Authority.Refuse("the window isn't barricaded");
                return false;
            }

            if (a == Action.WindowHit && x > 1000f)
            {
                _coop.Authority.Refuse(
                    $"a hit of {x:0} is more than any weapon does");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Make a WorldAction's call on our window. False: it isn't
        /// here.
        /// </summary>
        internal bool Apply(Action a, ObjRef obj, in ActionValues values)
        {
            float x = values.X, y = values.Y;
            bool f1 = values.F1, f2 = values.F2;
            string str = values.Str;
            var window = WorldActions.Got<Window>(a, obj);
            if (window == null)
            {
                return false;
            }

            switch (a)
            {
                case Action.WindowBarricade:
                    // as for a door
                    window.barricadeState = 2;
                    window.barricade((int)x, f1);
                    break;
                case Action.WindowDestroyBarricade:
                    window.destroyBarricade(f1);
                    break;
                case Action.WindowHit:
                    window.getHit((int)x, null, f1);
                    break;
            }

            return true;
        }

        internal HostWindow HostOf(Window window) => new HostWindow
        {
            Barricaded = window.barricaded,
            BarricadeHealth = window.barricadeHealth,
        };

        /// <summary>
        /// Client: put our window as the host has it.
        /// </summary>
        internal void RollBack(Window window, in HostWindow host)
        {
            if (host.Barricaded && !window.barricaded)
            {
                window.barricadeState = 2;
                window.barricade(host.BarricadeHealth, false);
            }
            else if (!host.Barricaded && window.barricaded)
            {
                window.destroyBarricade(false);
            }

            window.barricadeHealth = host.BarricadeHealth;
        }
    }
}
