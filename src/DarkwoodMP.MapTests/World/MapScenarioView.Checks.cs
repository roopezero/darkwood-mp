using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioView
    {
        // ---- the checks -----------------------------------------------------

        private static bool IsOurs(Collider c)
        {
            for (var t = c.transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith("DarkwoodMP_"))
                {
                    return true;
                }
            }

            return c.GetComponentInParent<Combat.NetPlayerProxy>() != null;
        }

        /// <summary>
        /// A collider whose saved object is switched off in the save (it
        /// shouldn't be there at all). Not inside a separate location: the game
        /// leaves Active off on many of a location's objects it shows (the
        /// village's border roots and trees, in every game alike), so there it
        /// says nothing.
        /// </summary>
        private static bool IsGhost(Collider c)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
            {
                return false;
            }

            for (var t = c.transform; t != null; t = t.parent)
            {
                if (t.name == "OutsideLocations")
                {
                    return false;
                }
            }

            for (var t = c.transform; t != null; t = t.parent)
            {
                var so = t.GetComponent<SaveableObject>();
                if (so != null && !so.Active)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> GhostBlockers(Light2D light)
        {
            var list = new List<string>();
            foreach (var c in Physics.OverlapSphere(
                light.transform.position, light.LightRadius, light.ShadowLayer))
            {
                if (c != null && (IsGhost(c) || IsOurs(c)))
                {
                    string path = Sync.ViewWatch.Path(c.transform);
                    int layer = c.gameObject.layer;
                    string kind;
                    if (IsOurs(c))
                    {
                        kind = ", the mod's";
                    }
                    else
                    {
                        kind = ", switched off in the save";
                    }

                    list.Add($"{path} [layer {layer}{kind}]");
                }
            }

            return list;
        }

        /// <summary>
        /// How much of the cone's mesh disagrees with rays cast from the light
        /// now: each vertex is a ray's end (UpdateRadialMesh), either where it
        /// hit (inside the radius) or the radius itself. A hit vertex wants our
        /// ray to hit there too; a free one wants our ray to reach (nearly) the
        /// radius. Rounding and shadow edges give a few disagreements; a mesh
        /// drawn somewhere else disagrees almost everywhere.
        /// </summary>
        private static float Mismatch(Light2D light, out string info)
        {
            info = "none";
            var mesh = light._mesh;
            if (mesh == null || mesh.vertexCount < 3)
            {
                info = "no mesh";
                return 0f;
            }

            var verts = mesh.vertices;
            var o = light.transform.position;
            float R = light.LightRadius;
            int n = 0, bad = 0, hits = 0;
            for (int i = 1; i < verts.Length; i++)
            {
                var wpt = light.transform.TransformPoint(verts[i]);
                var d = wpt - o;
                float dist = d.magnitude;
                if (dist < 5f)
                {
                    continue;
                }

                n++;
                bool hit = Physics.Raycast(
                    o, d / dist, out var h, R, light.ShadowLayer);
                if (dist < R - 3f)
                {
                    hits++;
                    if (!hit
                        || Mathf.Abs(h.distance - dist) > Mathf.Max(
                            20f, dist * 0.15f))
                    {
                        bad++;
                    }
                }
                else if (hit && h.distance < R * 0.8f)
                {
                    bad++;
                }
            }

            if (n == 0)
            {
                info = "empty";
                return 0f;
            }

            float m = (float)bad / n;
            info = $"{n} rays ({hits} hit), {bad} not matching ours ({m:P0})";
            return m;
        }

        /// <summary>
        /// How far our view reaches on each side of where our player faces
        /// (rays across the cone), and what stops it.
        /// </summary>
        private static string Sides(Player p, Light2D light)
        {
            var o = light.transform.position;
            var f = p.transform.up;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f)
            {
                return "no facing";
            }

            f.Normalize();
            float cone = Mathf.Clamp(light.LightConeAngle, 0f, 360f),
                R = light.LightRadius;
            float left = 0f, right = 0f;
            int nl = 0, nr = 0;
            var stopL = new Dictionary<string, int>();
            var stopR = new Dictionary<string, int>();
            for (int i = 0; i <= 40; i++)
            {
                // negative: to the left (counter-clockwise seen from above)
                float ang = -cone / 2f + cone * i / 40f;
                var dir = Quaternion.AngleAxis(ang, Vector3.up) * f;
                bool hit =
                    Physics.Raycast(o, dir, out var h, R, light.ShadowLayer);
                float reach = 1f;
                if (hit)
                {
                    reach = h.distance / R;
                }

                Dictionary<string, int> stops;
                if (ang < 0f)
                {
                    stops = stopL;
                }
                else
                {
                    stops = stopR;
                }

                if (hit)
                {
                    string k = h.collider.name;
                    if (stops.TryGetValue(k, out int c))
                    {
                        stops[k] = c + 1;
                    }
                    else
                    {
                        stops[k] = 1;
                    }
                }

                if (ang < 0f)
                {
                    left += reach;
                    nl++;
                }
                else if (ang > 0f)
                {
                    right += reach;
                    nr++;
                }
            }

            string top(Dictionary<string, int> s)
            {
                if (s.Count == 0)
                {
                    return "nothing";
                }

                return string.Join(", ", s.OrderByDescending(x => x.Value)
                        .Take(3).Select(x => $"{x.Key} x{x.Value}"));
            }

            float leftReach = 0f;
            if (nl > 0)
            {
                leftReach = left / nl;
            }

            float rightReach = 0f;
            if (nr > 0)
            {
                rightReach = right / nr;
            }

            return $"reach left {leftReach:P0} (stopped by {top(stopL)}), "
                + $"right {rightReach:P0} (stopped by {top(stopR)})";
        }
    }
}
