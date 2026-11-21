using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>What thrown things did to the dogs.</summary>
    public static partial class MapScenarioItems2
    {
        /// <summary>
        /// "i2-heard NAME" (host): the NAME dog heard a sound (not a dangerous
        /// one) from the thrown thing since i2-note.
        /// </summary>
        private static IEnumerator HeardStep(string[] a, Result r)
        {
            string food = "meat";
            if (a.Length > 0)
            {
                food = a[0];
            }

            var d = DogOf(food);
            if (d == null)
            {
                r.Fail("no dog");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 6f;
            while (!Heard.Any(h => h.c == d && !h.dangerous)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            var got = Heard.Where(h => h.c == d).ToList();
            string prefix = $"{d.name} heard {got.Count} sound(s): ";
            string sounds = string.Join(", ", got.Take(4).Select(h =>
            {
                string danger = "";
                if (h.dangerous)
                {
                    danger = " (dangerous)";
                }

                return $"{Fmt(h.pos)}{danger}";
            }));

            r.Info = prefix + sounds + $"; behaviour {d.behaviour}";
            if (!got.Any(h => !h.dangerous))
            {
                r.Fail("it heard nothing: " + r.Info);
            }
        }

        /// <summary>
        /// "i2-eats NAME" (host): the NAME dog goes for the thrown lure and
        /// eats it up.
        /// </summary>
        private static IEnumerator Eats(string[] a, Result r)
        {
            string food = "meat";
            if (a.Length > 0)
            {
                food = a[0];
            }

            var d = DogOf(food);
            if (d == null)
            {
                r.Fail("no dog");
                yield break;
            }

            var lure = Object.FindObjectsOfType<Lure>().Where(l => l != null)
                .OrderBy(
                    l => StateDump.Flat(
                        l.transform.position
                            - d.transform.position)).FirstOrDefault();
            if (lure == null)
            {
                r.Fail("no lure here");
                yield break;
            }

            string lname = lure.name;
            bool went = false, ate = false;
            float until = Time.realtimeSinceStartup + 30f;
            while (lure != null && !ate && Time.realtimeSinceStartup < until)
            {
                went |= d.headingForLure;
                ate |= d.eating;
                yield return new WaitForSeconds(0.2f);
            }

            // A piece of meat lasts the game a long while (its health is in the
            // thousands, one bite a frame of the eating loop): once the dog is
            // eating, leave it one bite, which the dog's own next bite takes.
            if (lure != null && ate && lure.health > 1)
            {
                lure.health = 1;
            }

            until = Time.realtimeSinceStartup + 20f;
            while (lure != null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            string text;
            if (lure == null)
            {
                text = "eaten up";
            }
            else
            {
                text = $"still there (health {lure.health})";
            }

            r.Info =
                $"{d.name} went for {lname}: {went}, ate: {ate}, the lure is "
                    + $"{text}";
            if (lure != null && d != null)
            {
                string targetName = "nothing";
                if (d.target != null)
                {
                    targetName = d.target.name;
                }

                float away = StateDump.Flat(
                    lure.transform.position - d.transform.position);
                r.Info +=
                    $"; the dog is {d.behaviour} after {targetName}, "
                        + $"{away:0} from the lure";
            }

            if (lure != null)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "i2-gone TYPE": our thrown TYPE (the last i2-throw) is gone from
        /// this game (eaten in the host's).
        /// </summary>
        private static IEnumerator Gone(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (_thrown != null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            if (_thrown == null)
            {
                r.Info = "gone";
            }
            else
            {
                r.Info =
                    $"{_thrown.name} still at "
                        + $"{Fmt(_thrown.transform.position)}";
            }

            if (_thrown != null)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "i2-gassed NAME" (host): the NAME dog was caught in the gas (the
        /// effect on it, or health lost since i2-note).
        /// </summary>
        private static IEnumerator Gassed(string[] a, Result r)
        {
            string name = "meat";
            if (a.Length > 0)
            {
                name = a[0];
            }

            var d = DogOf(name);
            if (d == null)
            {
                r.Fail("no dog");
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

            bool gas = false;
            float until = Time.realtimeSinceStartup + 6f;
            while (!(gas = d.effects != null && d.effects.hasEffectType(
                CharacterEffectType.gas)) && d.health >= was
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            r.Info =
                $"{d.name}: gas effect {gas}, health {was:0} -> {d.health:0}";
            if (!gas && d.health >= was)
            {
                r.Fail(r.Info);
            }
        }
    }
}
