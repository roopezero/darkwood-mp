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
        /// window, as this world is now (WindowRules.MayAct). Refuses the
        /// request if not.
        /// </summary>
        internal bool Allowed(Action a, ObjRef obj, float x)
        {
            var facts = FactsOf(ObjRef.Resolve<Window>(obj));
            var verdict = WindowRules.MayAct(facts, (WindowAction)a, x);
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the host's check knows of a window and the requesting
        /// player: the only code that reads a window for WindowRules.
        /// window may be null (not in this world).
        /// </summary>
        private WindowFacts FactsOf(Window window)
        {
            if (window == null)
            {
                return new WindowFacts();
            }

            var reach = _coop.Authority.ReachOf(
                window.transform.position,
                window.name);
            return new WindowFacts
            {
                Exists = true,
                Requester = reach,
                Barricaded = window.barricaded,
            };
        }

        /// <summary>
        /// Make a WorldAction's call on our window. False: it isn't here.
        /// </summary>
        internal bool Apply(Action a, ObjRef obj, in ActionValues values)
        {
            var window = WorldActions.Got<Window>(a, obj);
            if (window == null)
            {
                return false;
            }

            switch ((WindowAction)a)
            {
                case WindowAction.Barricade:
                    // as for a door
                    window.barricadeState = 2;
                    window.barricade((int)values.X, values.F1);
                    break;
                case WindowAction.DestroyBarricade:
                    window.destroyBarricade(values.F1);
                    break;
                case WindowAction.Hit:
                    window.getHit((int)values.X, null, values.F1);
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
        /// Client: put our window as the host has it (WindowRules.RollBack).
        /// </summary>
        internal void RollBack(Window window, in HostWindow host)
        {
            var fix = WindowRules.RollBack(host.Barricaded, window.barricaded);
            if (fix == WindowFix.Barricade)
            {
                window.barricadeState = 2;
                window.barricade(host.BarricadeHealth, false);
            }
            else if (fix == WindowFix.RemoveBarricade)
            {
                window.destroyBarricade(false);
            }

            window.barricadeHealth = host.BarricadeHealth;
        }
    }
}
