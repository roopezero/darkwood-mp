using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Sync.Players;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioView
    {
        // ---- finding the spot -----------------------------------------------

        private static List<GameObject> Matches(string pattern)
        {
            var c = Centre;
            return Object.FindObjectsOfType<Transform>(true)
                .Where(
                    t => t != null && t.gameObject.scene.IsValid()
                        && t.name.IndexOf(
                            pattern,
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.gameObject)
                // One per spot: a prefab's children often carry the same name
                // part.
                .GroupBy(
                    g => new Vector2Int(
                        Mathf.RoundToInt(g.transform.position.x / 100f),
                        Mathf.RoundToInt(g.transform.position.z / 100f)))
                .Select(g => g.OrderBy(x => x.transform.GetSiblingIndex())
                    .First())
                .OrderBy(g => StateDump.Flat(g.transform.position - c))
                .ToList();
        }

        private static string Describe(GameObject g)
        {
            var so = g.GetComponentInParent<SaveableObject>(true);
            var cols = g.GetComponentsInChildren<Collider>(true)
                .Where(
                    x => (ViewWatch.HideMask & (1 << x.gameObject.layer))
                        != 0).ToList();
            string path = ViewWatch.Path(g.transform);
            string spot = Fmt(g.transform.position);
            float distance = StateDump.Flat(g.transform.position - Centre);
            bool active = g.activeInHierarchy;
            string saved;
            if (so == null)
            {
                saved = "-";
            }
            else
            {
                saved = so.Active.ToString();
            }

            string colliders = "";
            if (cols.Count != 0)
            {
                string list = string.Join(", ", cols.Take(4).Select(x =>
                {
                    string name = x.name;
                    int layer = x.gameObject.layer;
                    string off = "";
                    if (!x.enabled)
                    {
                        off = ":off";
                    }

                    return $"{name}:{layer}{off}";
                }));

                colliders = $" [{list}]";
            }

            return $"{path} at {spot} ({distance:0} from the centre), " +
                   $"active {active}, saved {saved}, " +
                   $"view-layer colliders {cols.Count}{colliders}";
        }

        private static IEnumerator Find(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: vw-find PATTERN [N]");
                yield break;
            }

            int n = 8;
            if (a.Length > 1)
            {
                n = int.Parse(a[1], CultureInfo.InvariantCulture);
            }

            var list = Matches(a[0]);
            r.Info = $"{list.Count} spots with '{a[0]}': " + string.Join(
                "; ", list.Take(n).Select(Describe));
            Plugin.Log.LogInfo($"[view] vw-find {a[0]}: {r.Info}");
        }

        private static IEnumerator MarkStep(string[] a, Result r)
        {
            if (a.Length < 2)
            {
                r.Fail("usage: vw-mark PATTERN INDEX");
                yield break;
            }

            var list = Matches(a[0]);
            int i = int.Parse(a[1], CultureInfo.InvariantCulture);
            if (i >= list.Count)
            {
                r.Fail($"only {list.Count} spots with '{a[0]}'");
                yield break;
            }

            _mark = new Vector3(
                list[i].transform.position.x, 0f, list[i].transform.position.z);
            _markName = list[i].name;
            r.Info = $"mark: {Describe(list[i])}";
        }
    }
}
