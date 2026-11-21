using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>The dogs: spawned, noted, hurt and killed.</summary>
    public static partial class MapScenarioItems2
    {
        // ---- the dogs -------------------------------------------------------

        private static readonly Dictionary<string, float> _noted =
            new Dictionary<string, float>();

        private static int _mark, _shotsNoted;

        private static HashSet<GameObject> _spawnedAtNote =
            new HashSet<GameObject>();

        private static HashSet<Burn> _burnsAtNote = new HashSet<Burn>();

        /// <summary>
        /// "i2-dog NAME [HEALTH] [--hungry]" (host): a Dog at NAME's spot, not
        /// set on anyone (with that much health). --hungry makes it very hungry
        /// (Character.veryHungry): it goes for food even while it's chasing
        /// someone.
        /// A dog that has seen the thrower (the noise lure brings it within
        /// sight of them) chases them, and a chasing dog that isn't very hungry
        /// passes over a lure (Character.checkForLure).
        /// </summary>
        private static IEnumerator Dog(string[] a, Result r)
        {
            bool hungry = System.Array.IndexOf(a, "--hungry") >= 0;
            a = System.Array.FindAll(a, s => s != "--hungry");
            string name = "gun";
            if (a.Length > 0)
            {
                name = a[0];
            }

            var spot = MapScenarioEnemies.FreeSpot(Spot(name));
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                P.gameObject, spot, 1f, "Dog", nocturnal: false,
                attackPlayer: false);
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            c.transform.position = spot;
            _dogs[name] = c;
            // it's set up (its health is 0 until then)
            yield return new WaitForSeconds(1.5f);
            if (a.Length > 1)
            {
                c.maxHealth = F(a[1]);
                c.health = F(a[1]);
            }

            if (hungry)
            {
                c.veryHungry = true;
            }

            _noted[name] = c.health;
            NetEnemies.Instance.NetIdOf(c, out uint id);
            string info =
                $"{c.name} #{id} at {Fmt(c.transform.position)}, health "
                    + $"{c.health}, eats {c.lureType}";
            if (c.veryHungry)
            {
                info += ", very hungry";
            }

            r.Info = info;
        }

        /// <summary>
        /// "i2-note [NAME]": note the dog's health here, the log's length and
        /// the other players' shots seen.
        /// </summary>
        private static IEnumerator Note(string[] a, Result r)
        {
            _mark = MapScenarioIssues.LogMark();
            _shotsNoted = Sync.PlayerFx.ShotsSeen;
            Heard.Clear();
            _spawnedAtNote = new HashSet<GameObject>(
                ThrownHits.Spawned.Where(g => g != null));
            _burnsAtNote = new HashSet<Burn>(Object.FindObjectsOfType<Burn>());
            if (a.Length > 0)
            {
                var d = DogOf(a[0]);
                if (d != null)
                {
                    _noted[a[0]] = d.health;
                }
            }

            string info = $"log line {_mark}, shots seen {_shotsNoted}";
            if (a.Length > 0 && _noted.TryGetValue(a[0], out float h))
            {
                info += $", {a[0]} dog health {h:0}";
            }

            r.Info = info;
            yield break;
        }

        /// <summary>
        /// "i2-hurt NAME" (host): the dog lost health since i2-note, and no hit
        /// on it was refused.
        /// </summary>
        private static IEnumerator Hurt(string[] a, Result r)
        {
            string name = "gun";
            if (a.Length > 0)
            {
                name = a[0];
            }

            yield return new WaitForSeconds(1f);
            var d = DogOf(name);
            if (d == null)
            {
                r.Fail($"no {name} dog");
                yield break;
            }

            float was;
            if (_noted.TryGetValue(name, out float h))
            {
                was = h;
            }
            else
            {
                was = d.maxHealth;
            }

            var refused = LogSince(_mark, @"refused EnemyHit");
            var capped =
                LogSince(_mark, @"\[authority\] .*'s hit on .* is more than");
            r.Info =
                $"{d.name}: health {was:0} -> {d.health:0}, alive {d.alive}; "
                    + $"{refused.Count} hit(s) refused, {capped.Count} capped";
            if (!(d.health < was) && d.alive)
            {
                r.Fail("no damage here: " + r.Info);
            }

            if (refused.Count > 0)
            {
                r.Fail(r.Info + ": " + refused[0].Trim());
            }

            _noted[name] = d.health;
        }

        /// <summary>
        /// "i2-shots-seen MIN": at least MIN of the other player's shots were
        /// shown on their puppet since i2-note.
        /// </summary>
        private static IEnumerator ShotsSeen(string[] a, Result r)
        {
            int want = 1;
            if (a.Length > 0)
            {
                want = int.Parse(a[0]);
            }

            float until = Time.realtimeSinceStartup + 5f;
            while (Sync.PlayerFx.ShotsSeen - _shotsNoted < want
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.3f);
            }

            int n = Sync.PlayerFx.ShotsSeen - _shotsNoted;
            r.Info = $"{n} shot(s) shown on the other player's puppet";
            if (n < want)
            {
                r.Fail(r.Info + $", not {want}");
            }
        }

        /// <summary>
        /// "i2-kill NAME" (host): the dog killed (as by our player), so it
        /// doesn't follow anyone around later steps.
        /// </summary>
        private static IEnumerator Kill(string[] a, Result r)
        {
            string name = "gun";
            if (a.Length > 0)
            {
                name = a[0];
            }

            var d = DogOf(name);
            if (d == null)
            {
                r.Info = "no dog (gone already)";
                yield break;
            }

            yield return KillAsPlayer(d, r, 1.5f);
        }

        /// <summary>
        /// Our player kills C (a hit of all its health and more); WAIT seconds
        /// later it must be dead.
        /// </summary>
        internal static IEnumerator KillAsPlayer(
            Character c, Result r, float wait)
        {
            if (c.alive)
            {
                AsPlayer(
                    () => c.getHit(
                        c.health + 500f, P.transform, false, true, false));
            }

            yield return new WaitForSeconds(wait);
            r.Info = $"{c.name} alive {c.alive}";
            if (c.alive)
            {
                r.Fail("it's still alive");
            }
        }
    }
}
