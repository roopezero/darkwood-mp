using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: the stand-in an enemy checking out a location chases, noted by
    /// checkOutLocation's prefix for the gotoNextWaypoint call it makes,
    /// which then starts at the waypoint nearest them. It lives as long as
    /// the game (ModHost.Chases): the test dummy brings stand-ins into a
    /// game played alone.
    /// </summary>
    internal sealed class JoinerChases
    {
        private NetPlayerProxy _chased;

        /// <summary>checkOutLocation is about to run for this enemy.</summary>
        public void Note(Character c)
        {
            _chased = null;
            var chase = FactsOf(c, out var proxy);
            if (JoinerAiRules.NotesChase(chase))
            {
                _chased = proxy;
            }
        }

        /// <summary>checkOutLocation threw or ended.</summary>
        public void Forget()
        {
            _chased = null;
        }

        /// <summary>
        /// gotoNextWaypoint: the waypoint nearest the noted stand-in, when
        /// none was given and the enemy checks out a location.
        /// </summary>
        public void Waypoint(Character c, ref Waypoint waypoint)
        {
            var proxy = _chased;
            var loc = c.locationCheckingOut;
            var step = JoinerAiRules.Waypoint(
                proxy != null, waypoint != null, loc != null);
            if (step == WaypointStep.Leave)
            {
                return;
            }

            // only the call checkOutLocation makes
            _chased = null;
            if (step == WaypointStep.Forget)
            {
                return;
            }

            Vector3 at = proxy.transform.position;
            var spots = new List<WaypointSpot>();
            foreach (var w in loc.waypointsInside)
            {
                float d = 0f;
                if (w != null)
                {
                    d = (w.transform.position - at).sqrMagnitude;
                }

                spots.Add(new WaypointSpot
                {
                    Exists = w != null,
                    SqrDistance = d,
                });
            }

            int best = JoinerAiRules.NearestWaypoint(spots);
            Waypoint pick = null;
            if (best >= 0)
            {
                pick = loc.waypointsInside[best];
            }

            waypoint = pick;
        }

        private static ChaseFacts FactsOf(
            Character c, out NetPlayerProxy proxy)
        {
            proxy = null;
            var chase = new ChaseFacts
            {
                On = Plugin.Instance.Host.Threats.Enabled,
            };

            if (!chase.On)
            {
                return chase;
            }

            chase = chase with { CheckingOut = c.checkingOutLocation };
            if (chase.CheckingOut)
            {
                return chase;
            }

            proxy = JoinerAi.ProxyOf(c.superTarget);
            if (proxy == null || !proxy.alive)
            {
                return chase with
                {
                    StandIn = proxy != null,
                };
            }

            return chase with
            {
                StandIn = true,
                StandInAlive = true,
                StandInInside = JoinerAi.InsideAt(proxy.transform.position),
            };
        }
    }
}
