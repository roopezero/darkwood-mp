using System;
using System.Collections;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps with several client games at once
    /// (tools/tests/map4_multi.py, run with tools/run-test.sh map4): an enemy's
    /// threat among several remote players, three players taking from one
    /// container slot at the same moment, two at one trader, things done while
    /// a third player is far away, and night attackers with players in
    /// different hideouts. Steps that happen "at the same moment" take an epoch
    /// second (the games share one machine's clock), which the driver fills in
    /// for a step run in several games at once ("c1+c2+c3").
    /// </summary>
    public static partial class MapScenarioMulti
    {
        private static Player P => Player.Instance;
        private static double Now =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        public static void Register()
        {
            Add("mt-stand", Stand, timeout: 60f);
            Add("mt-home", HomeStep, timeout: 90f);
            Add("mt-tough", Tough);
            Add("mt-debug-threat", (a, r) =>
            {
                Threat.Debug = a.Length == 0 || a[0] == "on";
                r.Info = $"threat log {Threat.Debug}";
                return Nothing();
            });

            Add("mt-die", Die, timeout: 60f);
            Add("mt-spawn", SpawnEnemy, timeout: 15f);
            Add("mt-hit", HitEnemy, timeout: 40f);
            Add("mt-target", Target, timeout: 40f);
            Add("mt-never", Never);
            Add("mt-dead", Dead, timeout: 20f);
            Add("mt-corpse", Corpse, timeout: 30f);
            Add("mt-grab", Grab, timeout: 40f);
            Add("mt-talk", Talk, timeout: 30f);
            Add("mt-door-drawn", DoorDrawn, timeout: 15f);
            Add("mt-hideout-go", HideoutGo, timeout: 90f);
            Add("mt-night", Night, timeout: 40f);
            Add("mt-locks", Locks);
            Add("mt-leave", Leave, timeout: 15f);
            Add("mt-open-free", OpenFree, timeout: 20f);
        }

        /// <summary>
        /// "mt-stand DX DZ": walk to the centre plus (DX, DZ).
        /// </summary>
        private static IEnumerator Stand(string[] a, Result r)
        {
            var to = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            yield return Walk(to, 40f);
            r.Info = Fmt(P.transform.position);
        }

        /// <summary>
        /// "mt-home": the test area is the centre again (after far-spot), and
        /// we walk back to it.
        /// </summary>
        private static IEnumerator HomeStep(string[] a, Result r)
        {
            if (Home.HasValue)
            {
                SetCentre(Home.Value);
            }

            yield return Walk(Centre, 40f);
            r.Info =
                $"centre {Fmt(Centre)}, we're at {Fmt(P.transform.position)}";
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        private static bool _tough;

        /// <summary>"mt-tough on|off": while on, our player is healed whenever
        /// below half health (an enemy biting them while the host's steps wait
        /// would kill them, and a death changes the story).</summary>
        private static IEnumerator Tough(string[] a, Result r)
        {
            bool on = a.Length == 0 || a[0] == "on";
            if (on && !_tough)
            {
                Plugin.Instance.StartCoroutine(KeepAlive());
            }

            _tough = on;
            r.Info = $"tough {on}";
            yield break;
        }

        private static IEnumerator KeepAlive()
        {
            while (true)
            {
                yield return null;
                if (!_tough)
                {
                    yield break;
                }

                var p = P;
                if (p != null && p.alive && p.health < p.maxHealth * 0.5f)
                {
                    p.bleeding = false;
                    p.setHealth(p.maxHealth);
                }
            }
        }

        /// <summary>"mt-die": our player dies (Player.die) and respawns; the
        /// death's story flags (player_diedAtLeastOneTime) must reach the other
        /// games.</summary>
        private static IEnumerator Die(string[] a, Result r)
        {
            var p = P;
            if (p == null || !p.alive)
            {
                r.Fail("not alive");
                yield break;
            }

            p.die();
            float until = Time.realtimeSinceStartup + 50f;
            yield return new WaitForSeconds(2f);
            while (Time.realtimeSinceStartup < until
                && !(P != null && P.alive && WorldShare.WorldReady))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(FlagsSync.Interval + 2f);
            r.Info =
                $"alive again {P != null && P.alive} at "
                    + $"{Fmt(P.transform.position)}";
            if (P == null || !P.alive)
            {
                r.Fail(r.Info);
            }
        }
    }
}
