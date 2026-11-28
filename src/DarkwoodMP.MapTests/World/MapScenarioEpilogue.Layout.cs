using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// The epilogue's layout check: the part each game built is the same.
    public static partial class MapScenarioEpilogue
    {
        /// <summary>
        /// "epilogue-layout [WORLD LOCAL]": the current part's layout (its
        /// location's solid things: name and where, rounded) as two digests,
        /// one of world spots (the location's spot and turn count) and one of
        /// spots inside the location. With the host's two, ours must match.
        /// The full list goes to BepInEx/dwmp-layout.txt, to diff by hand.
        /// </summary>
        private static IEnumerator Layout(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until
                && !(P != null && P.inEpilogue && D != null && D.dreaming
                    && !D.switchingDream && D.dreamLocation != null))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(3f);
            Location loc = null;
            if (D != null)
            {
                loc = D.dreamLocation;
            }

            if (loc == null)
            {
                r.Fail($"no dream location: {Where()}");
                yield break;
            }

            var world = new List<string>();
            var local = new List<string>();
            foreach (var c in loc.GetComponentsInChildren<Collider>(false))
            {
                if (c == null || !c.enabled || c.isTrigger
                    || c.GetComponentInParent<Rigidbody>() != null)
                {
                    continue;
                }

                Vector3 w = c.transform.position;
                Vector3 l = loc.transform.InverseTransformPoint(w);
                world.Add($"{c.name} {Round(w.x)} {Round(w.z)}");
                local.Add($"{c.name} {Round(l.x)} {Round(l.z)}");
            }

            world.Sort(System.StringComparer.Ordinal);
            local.Sort(System.StringComparer.Ordinal);
            string wd = "W" + Digest(world);
            string ld = "L" + Digest(local);
            int turn = Mathf.RoundToInt(loc.transform.eulerAngles.y);
            Vector3 at = loc.transform.position;
            WriteLayout(loc, local);
            r.Info = $"{wd} {ld} n={world.Count} {loc.name} turned {turn} "
                + $"at {at.x:0},{at.z:0}";
            if (a.Length >= 2 && (a[0] != wd || a[1] != ld))
            {
                r.Fail($"not the host's layout ({a[0]} {a[1]}): {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-flag NAME": set the story flag NAME (true), as the game's
        /// story does.
        /// </summary>
        private static IEnumerator SetFlag(string[] a, Result r)
        {
            Singleton<Flags>.Instance.setFlag(a[0], true);
            yield return new WaitForSeconds(0.5f);
            bool set = Singleton<Flags>.Instance.isFlagTrue(a[0]);
            r.Info = $"{a[0]} {set}";
            if (!set)
            {
                r.Fail($"{a[0]} didn't change");
            }
        }

        private static int Round(float v) => Mathf.RoundToInt(v / 25f) * 25;

        private static string Digest(List<string> lines)
        {
            uint h = 2166136261;
            foreach (char ch in string.Join("\n", lines))
            {
                h = (h ^ ch) * 16777619;
            }

            return h.ToString("x8");
        }

        private static void WriteLayout(Location loc, List<string> local)
        {
            try
            {
                string path = Path.Combine(
                    BepInEx.Paths.BepInExRootPath, "dwmp-layout.txt");
                var lines = new List<string>
                {
                    $"{loc.name} at {loc.transform.position} turned "
                        + $"{loc.transform.eulerAngles.y}",
                };

                lines.AddRange(local.Distinct());
                File.WriteAllLines(path, lines);
            }
            catch (IOException e)
            {
                Plugin.Log.LogWarning($"[map] couldn't write the layout: {e}");
            }
        }
    }
}
