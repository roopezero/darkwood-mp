using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 11 dreams ------------------------------------------------------

        // clock-note's minute
        private static int _noted;
        // the dream's minute, from "dream start"
        private static int _dreamTime;

        /// <summary>"dream start|end": as if our player went into a dream
        /// (Dreams.startDreaming: the world's time kept in timeCopy, the clock
        /// showing the dream's, 5 hours on) or woke (endDreaming: the clock
        /// back to timeCopy). A joiner uses the dream-switching flag, which
        /// TimeSync treats the same, so its game doesn't also wake its own
        /// enemies near it for a dream that isn't there.</summary>
        private static IEnumerator Dream(string[] a, Result r)
        {
            var d = Singleton<Dreams>.Instance;
            var c = Singleton<Controller>.Instance;
            bool client =
                Net.Session.Instance != null && Net.Session.Instance.IsClient;
            if (d == null || c == null)
            {
                r.Fail("no Dreams or Controller");
                yield break;
            }

            if (a.Length > 0 && a[0] == "start")
            {
                d.timeCopy = c.CurrentTime;
                if (client)
                {
                    d.switchingDream = true;
                }
                else
                {
                    d.dreaming = true;
                }

                c.CurrentTime = (c.CurrentTime + 300) % 1440;
                _dreamTime = c.CurrentTime;
                r.Info =
                    $"world time {d.timeCopy:0} kept, dream at {c.CurrentTime}";
            }
            else
            {
                c.CurrentTime = (int)d.timeCopy;
                d.dreaming = false;
                d.switchingDream = false;
                c.refreshTime(afterGameLoad: true);
                r.Info = $"woke at {c.CurrentTime}";
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>"clock-check noted|dream [SECS]": after SECS (3) real
        /// seconds, our clock is still the minute noted with clock-note (within
        /// 3: the host's world clock stood still while it dreamed), or still
        /// our dream's.</summary>
        private static IEnumerator ClockCheck(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            float secs = 3f;
            if (a.Length > 1)
            {
                secs = F(a[1]);
            }

            yield return new WaitForSecondsRealtime(secs);
            int want;
            if (a.Length > 0 && a[0] == "dream")
            {
                want = _dreamTime;
            }
            else
            {
                want = _noted;
            }

            int off = Math.Abs(
                ((c.CurrentTime - want) % 1440 + 1440 + 720) % 1440 - 720);
            r.Info = $"clock {c.CurrentTime} (wanted {want}, {off} off)";
            if (off > 3)
            {
                r.Fail(r.Info);
            }
        }
    }
}
