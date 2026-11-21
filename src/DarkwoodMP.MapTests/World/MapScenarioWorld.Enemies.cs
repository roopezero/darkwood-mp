using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps that spawn and kill enemies.</summary>
    public static partial class MapScenarioWorld
    {
        /// <summary>
        /// "spawn TYPE": the host spawns an enemy near its player
        /// (CharacterSpawner).
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            if (Net.Session.Instance != null && Net.Session.Instance.IsClient)
            {
                r.Fail("enemies are the host's");
                yield break;
            }

            string kind = "Dog";
            if (a.Length > 0)
            {
                kind = a[0];
            }

            var c = TestWorld.SpawnEnemy(kind);
            yield return new WaitForSeconds(1f);
            if (c != null)
            {
                r.Info = $"{c.name} {Ref(c)} at {Fmt(c.transform.position)}";
            }
            else
            {
                r.Info = "none";
            }

            if (c == null)
            {
                r.Fail("nothing spawned");
            }
        }

        /// <summary>
        /// The host's live enemy nearest to our player, within radius of the
        /// centre.
        /// </summary>
        internal static Character HostEnemyNear(float radius)
        {
            var ne = Combat.NetEnemies.Instance;
            if (ne == null)
            {
                return null;
            }

            return Nearest(
                Object.FindObjectsOfType<Character>().Where(
                    x => x.alive && Combat.NetEnemies.Eligible(x)
                        && ne.NetIdOf(x, out _)
                        && StateDump.Flat(x.transform.position - Centre)
                            < radius), P.transform.position);
        }

        /// <summary>"kill-enemy": our player hits the host's enemy nearest to
        /// us until it's dead (a client's hits go to the host, capped by its
        /// weapon).</summary>
        private static IEnumerator KillEnemy(string[] a, Result r)
        {
            var c = HostEnemyNear(1500f);
            if (c == null)
            {
                r.Fail("no enemy of the host's here");
                yield break;
            }

            string what = $"{c.name} {Ref(c)}";
            int hits = 0;
            while (c != null && c.alive && hits < 40)
            {
                yield return Walk(c.transform.position, 80f);
                hits++;
                AsPlayer(
                    () => c.getHit(
                        60f, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSeconds(0.4f);
            }

            // a client settles the corpse 2 s after it dies
            yield return new WaitForSeconds(3f);
            bool alive = c != null && c.alive;
            bool searchable = c != null && c.GetComponent<Item>() != null;
            string at = "-";
            if (c != null)
            {
                at = Fmt(c.transform.position);
            }

            r.Info =
                $"{what}: alive={alive} after {hits} hits, "
                    + $"searchable={searchable} at {at}";
            if (c != null && c.alive)
            {
                r.Fail($"{r.Info}: still alive");
            }

            yield return Walk(Centre, 60f);
        }
    }
}
