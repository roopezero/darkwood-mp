using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Checks of the forest spirit.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 51: the forest spirit ------------------------------------------

        /// <summary>
        /// "fx-inside": walk onto the inside ground (Ground.isInside) nearest
        /// the centre: the hideout.
        /// </summary>
        private static IEnumerator FxInside(string[] a, Result r)
        {
            var spot = Inside();
            if (!spot.HasValue)
            {
                r.Fail("no inside ground near the centre");
                yield break;
            }

            yield return Walk(spot.Value, 20f);
            yield return new WaitForSeconds(1f);
            r.Info =
                $"at {Fmt(P.transform.position)}: inside {P.isInside} (ground "
                    + $"there {JoinerAi.InsideAt(P.transform.position)})";
            if (!JoinerAi.InsideAt(P.transform.position))
            {
                r.Fail($"not on inside ground: {r.Info}");
            }
        }

        private static Vector3? Inside()
        {
            Vector3? best = null;
            float bd = float.MaxValue;
            foreach (var g in Object.FindObjectsOfType<Ground>())
            {
                if (g == null || !g.isInside)
                {
                    continue;
                }

                var col = g.GetComponent<Collider>();
                if (col == null)
                {
                    continue;
                }

                var c = col.bounds.center;
                c.y = P.transform.position.y;
                float d = StateDump.Flat(c - Centre);
                if (d < bd && JoinerAi.InsideAt(c))
                {
                    bd = d;
                    best = c;
                }
            }

            return best;
        }

        private static readonly System.Reflection.MethodInfo CheckStuff =
            AccessTools.Method(typeof(Character), "checkStuff");

        /// <summary>"spirit leaves|stays" (host): a dog made a forest spirit
        /// after the client's proxy runs its check (Character.checkStuff): it
        /// leaves when the client is inside (whatever the host is), stays when
        /// they aren't.</summary>
        private static IEnumerator Spirit(string[] a, Result r)
        {
            bool leaves = a.Length == 0 || a[0] == "leaves";
            NetPlayerProxy proxy =
                NetPlayerProxy.All.Values.FirstOrDefault(x => x != null);
            if (proxy == null)
            {
                r.Fail("no proxy");
                yield break;
            }

            var c = SpawnAt(Spot("spirit"));
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            yield return new WaitForSeconds(1.5f);
            bool clientInside = JoinerAi.InsideAt(proxy.transform.position);
            string state =
                $"client inside {clientInside}, we inside {P.isInside}";
            c.forestSpirit = true;
            c.target = proxy.transform;
            CheckStuff.Invoke(c, null);
            if (c != null)
            {
                // its own checks from now on are a dog's
                c.forestSpirit = false;
            }

            // removeMe destroys a spirit 3 s later
            yield return new WaitForSeconds(4f);
            bool gone = c == null;
            string text = "stayed";
            if (gone)
            {
                text = "left";
            }

            r.Info = $"{state}: the spirit {text}";
            if (!gone)
            {
                Object.Destroy(c.gameObject);
            }

            // leaves: the client inside, we outside (the old check, on our
            // player, would keep it); stays: the other way round.
            if (clientInside != leaves || P.isInside == leaves)
            {
                r.Fail(
                    $"the players aren't where the step needs them: {r.Info}");
            }
            else if (gone != leaves)
            {
                r.Fail(r.Info);
            }
        }
    }
}
