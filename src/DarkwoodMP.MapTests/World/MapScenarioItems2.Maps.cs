using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Map items read.</summary>
    public static partial class MapScenarioItems2
    {
        // ---- map items ------------------------------------------------------

        private static readonly string[] Maps =
        {
            "map_radioTower",
            "map_trainWreck",
            "map_villageTree",
            "map_pigShed",
            "map_mushrooms",
            "map_dziury",
            "map_borderBurned",
            "map_piotrekPigsheds",
            "map_doctorEntrance",
            "map",
            "map_bio2",
            "map_bio3",
            "map_doctor_ch2"
        };

        private static string _map;

        private static List<Location> PlacesOf(string type)
        {
            var it = Singleton<ItemsDatabase>.Instance.getItem(
                type, instantiate: false);
            var wg = Singleton<WorldGenerator>.Instance;
            if (it == null || !it.isMap || wg == null)
            {
                return new List<Location>();
            }

            return it.locations.Where(n => n != null)
                .Select(n => wg.getLocation(n)).Where(l => l != null).ToList();
        }

        private static bool Shown(Location l)
        {
            var el = l.GetComponent<MapElement>();
            return Singleton<UI>.Instance.journal.locationsDict.ContainsKey(
                Core.getTrueLocationName(l.name)) && (el == null || el.isOnMap);
        }

        /// <summary>
        /// "i2-map-pick": the first map item none of whose places are in our
        /// journal yet (the same one in both games).
        /// </summary>
        private static IEnumerator MapPick(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            _map = Maps.FirstOrDefault(m =>
            {
                var ls = PlacesOf(m);
                return ls.Count > 0
                    && ls.All(
                        l => !j.locationsDict.ContainsKey(
                            Core.getTrueLocationName(l.name)));
            });

            if (_map == null)
            {
                r.Fail("every map's places are in the journal already");
                yield break;
            }

            string places =
                string.Join(", ", PlacesOf(_map).Select(l => l.name));
            r.Info =
                $"{_map}: "
                    + $"{places}";
        }

        /// <summary>
        /// "i2-map-use": read the picked map (InvItemClass.use: its places in
        /// the journal and on the map).
        /// </summary>
        private static IEnumerator MapUse(string[] a, Result r)
        {
            if (_map == null)
            {
                r.Fail("no map picked");
                yield break;
            }

            // In our bags, as a map is read from there (use() takes it out of
            // its slot).
            GiveBag(_map, 1);
            var item = _bagSlot.invItem;
            try
            {
                AsPlayer(() => item.use());
            }
            finally
            {
                Restore();
            }

            yield return new WaitForSeconds(0.5f);
            var ls = PlacesOf(_map);
            r.Info = $"{_map}: " + string.Join(", ", ls.Select(l =>
            {
                string name = l.name;
                string state = "hidden";
                if (Shown(l))
                {
                    state = "shown";
                }

                return $"{name} {state}";
            }));

            if (!ls.All(Shown))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "i2-map-check": the other player's picked map's places are in our
        /// journal and shown on our map.
        /// </summary>
        private static IEnumerator MapCheck(string[] a, Result r)
        {
            if (_map == null)
            {
                r.Fail("no map picked");
                yield break;
            }

            var ls = PlacesOf(_map);
            // the story state goes out every couple of seconds
            float until = Time.realtimeSinceStartup + 12f;
            while (!ls.All(Shown) && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }

            var j = Singleton<UI>.Instance.journal;
            r.Info = $"{_map}: " + string.Join(", ", ls.Select(l =>
            {
                string name = l.name;
                bool inJournal = j.locationsDict.ContainsKey(
                    Core.getTrueLocationName(l.name));
                MapElement element = l.GetComponent<MapElement>();
                string onMap = "no element";
                if (element != null)
                {
                    onMap = element.isOnMap.ToString();
                }

                return $"{name} journal {inJournal}, map {onMap}";
            }));

            if (!ls.All(Shown))
            {
                r.Fail(r.Info);
            }
        }
    }
}
