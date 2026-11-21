using System.Collections;
using System.Linq;
using DarkwoodMP.Net;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>The porter whistle's steps.</summary>
    public static partial class MapScenarioItems1
    {
        // ---- the porter whistle ---------------------------------------------

        /// <summary>
        /// The nearest big location to the centre where the porter can come.
        /// </summary>
        private static Location PorterPlace() =>
            Object.FindObjectsOfType<Location>().Where(
                l => l != null && l.porterPosition != null && !l.isSubLocation)
                .OrderBy(
                    l => StateDump.Flat(
                        l.porterPosition.transform.position
                            - Centre)).FirstOrDefault();

        private static bool PorterThere(Location loc) =>
            loc.porter != null
                || Object.FindObjectsOfType<PorterSpawner>().Any(
                    s => s != null
                        && StateDump.Flat(
                            s.transform.position
                                - loc.porterPosition.transform.position) < 50f);

        private static string PorterInfo(Location loc)
        {
            string name = loc.name;
            string state;
            if (loc.porter != null)
            {
                state = "here";
            }
            else if (PorterThere(loc))
            {
                state = "on its way";
            }
            else
            {
                state = "not called";
            }

            return $"at {name}: porter {state}";
        }

        /// <summary>
        /// "porter-ready" (host): daytime, the porter neither on the way nor
        /// there, so a whistle calls it.
        /// </summary>
        private static IEnumerator PorterReady(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null)
            {
                r.Fail("no place for the porter near the test area");
                yield break;
            }

            var flags = Singleton<Flags>.Instance;
            if (!Core.isDay())
            {
                TestWorld.SetTime(12f * 60f);
            }

            if (flags.isFlagTrue("porter_inTransit"))
            {
                flags.setFlag("porter_inTransit", activeModifier: false);
            }

            if (loc.porter != null)
            {
                Object.Destroy(loc.porter);
                loc.porter = null;
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"{loc.name} porter at "
                    + $"{Fmt(loc.porterPosition.transform.position)}; "
                    + $"day={Core.isDay()} "
                    + $"killed={flags.isFlagTrue("porter_killed")}";
            if (!Core.isDay() || flags.isFlagTrue("porter_killed"))
            {
                r.Fail($"{r.Info}: the porter can't come");
            }
        }

        /// <summary>
        /// "whistle" (client): our player blows the porter whistle at the
        /// porter's place; the host calls it, here too.
        /// </summary>
        private static IEnumerator Whistle(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null)
            {
                r.Fail("no place for the porter near the test area");
                yield break;
            }

            yield return Walk(loc.porterPosition.transform.position, 150f);
            P.whereAmI.checkWhereAmI();
            if (P.whereAmI.bigLocation != loc)
            {
                Location bigLocation = P.whereAmI.bigLocation;
                string name = null;
                if (bigLocation != null)
                {
                    name = bigLocation.name;
                }

                if (name == null)
                {
                    name = "no big location";
                }

                r.Fail($"we're in {name}, not {loc.name}");
                yield break;
            }

            if (PorterThere(loc))
            {
                r.Fail("the porter is there already");
                yield break;
            }

            int refused = Coop.Current.Authority.RolledBack;
            AsPlayer(() => new InvItemClass("porterWhistle", 1f, 1).use());
            float giveUp = Time.realtimeSinceStartup + 10f;
            while (!PorterThere(loc)
                && Coop.Current.Authority.RolledBack == refused
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            r.Info = PorterInfo(loc);
            if (Coop.Current.Authority.RolledBack != refused)
            {
                r.Fail($"{r.Info}: the host refused the call");
            }
            else if (!PorterThere(loc))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "porter-came": the porter (or its spawner, which waits until our
        /// player looks away) is at the place.
        /// </summary>
        private static IEnumerator PorterCame(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null)
            {
                r.Fail("no place for the porter near the test area");
                yield break;
            }

            float giveUp = Time.realtimeSinceStartup + 10f;
            while (!PorterThere(loc) && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            r.Info = PorterInfo(loc);
            if (!PorterThere(loc))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "porter-forged" (client): ask for the porter at another place for
        /// it, where our player isn't. Refused.
        /// </summary>
        private static IEnumerator PorterForged(string[] a, Result r)
        {
            var near = PorterPlace();
            var far = Object.FindObjectsOfType<Location>().Where(
                l => l != null && l.porterPosition != null && !l.isSubLocation
                    && l != near)
                .OrderByDescending(
                    l => StateDump.Flat(
                        l.porterPosition.transform.position
                            - P.transform.position)).FirstOrDefault();
            if (far == null)
            {
                r.Fail("only one place for the porter");
                yield break;
            }

            var w = S.Begin(MsgType.PorterCalled);
            w.Write(far.porterPosition.transform.position);
            bool? ok = null;
            string why = null;
            Coop.Current.Authority.Request(
                w, $"forged porter call at {far.name}",
                (o, reason, res) =>
            {
                ok = o;
                why = reason;
            }, inventory: false);

            float giveUp = Time.realtimeSinceStartup + 8f;
            while (ok == null && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            string answer;
            if (ok == null)
            {
                answer = "no answer";
            }
            else if (ok.Value)
            {
                answer = "allowed";
            }
            else
            {
                answer = "refused: " + why;
            }

            r.Info = $"at {far.name}: " + answer;
            if (ok != false)
            {
                r.Fail($"{r.Info}: the host didn't refuse it");
            }
        }
    }
}
