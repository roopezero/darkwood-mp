using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a generator's running sound. Item.turnOn starts
    /// the ItemSounds loop each time it's called, also on an item that is
    /// on already, and keeps only the newest: an older loop no switch-off
    /// stops. The morning after a night calls it again on everything the
    /// hideout's generator powers (Location.enableAllLights, from
    /// Controller.startAfterNight, Item.enable), the generator's own Item
    /// among them. Steps: "gen-sound NAME N", "gen-dawn NAME". Listed in
    /// tools/tests/map_scenario_generator.py.
    /// </summary>
    public static class MapScenarioGenerator
    {
        public static void Register()
        {
            Add("gen-sound", Sound);
            Add("gen-dawn", Dawn);
        }

        private static Generator Find(string spec)
        {
            return MapScenarioWorld.Find<Generator>(spec);
        }

        /// <summary>
        /// "gen-sound NAME N": N of the generator's start or loop sounds
        /// play under it (none once it is off).
        /// </summary>
        private static IEnumerator Sound(string[] a, Result r)
        {
            var g = Find(a[0]);
            if (g == null)
            {
                r.Fail($"no generator called {a[0]}");
                yield break;
            }

            int want = int.Parse(a[1]);
            // a loop's stop fades out for a moment
            yield return new WaitForSeconds(1f);
            var s = g.GetComponent<ItemSounds>();
            if (s == null)
            {
                r.Fail($"{g.name} has no ItemSounds");
                yield break;
            }

            int n = g.GetComponentsInChildren<AudioObject>(true).Count(
                o => o != null && o.IsPlaying()
                    && (o.audioID == s.loopSound
                        || o.audioID == s.startSound));
            var item = g.GetComponent<Item>();
            bool own = item != null && g.powerItems.Contains(item);
            r.Info = $"{g.name} on={g.isOn} sounds={n} (loop "
                + $"'{s.loopSound}', start '{s.startSound}') "
                + $"own item powered={own}";
            if (n != want)
            {
                r.Fail($"{r.Info}: want {want}");
            }
        }

        /// <summary>
        /// "gen-dawn NAME": what the morning after a night does to the
        /// generator's hideout: the game's Location.enableAllLights for the
        /// location it powers, or Item.enable on each of its power items.
        /// </summary>
        private static IEnumerator Dawn(string[] a, Result r)
        {
            var g = Find(a[0]);
            if (g == null)
            {
                r.Fail($"no generator called {a[0]}");
                yield break;
            }

            var at = Object.FindObjectsOfType<Location>()
                .FirstOrDefault(l => l != null && l.generator == g);
            if (at != null)
            {
                at.enableAllLights();
                r.Info = $"{at.name}.enableAllLights";
            }
            else
            {
                foreach (var i in g.powerItems)
                {
                    if (i != null)
                    {
                        i.enable();
                    }
                }

                r.Info = $"enable on {g.powerItems.Count} power items";
            }

            yield return new WaitForSeconds(1f);
        }
    }
}
