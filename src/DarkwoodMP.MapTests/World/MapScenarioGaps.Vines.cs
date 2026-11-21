using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 65 vines -------------------------------------------------------

        private static ChainParent Vine() => NearestAll<ChainParent>(
            Spot("vine"),
            cp => StateDump.Flat(cp.transform.position - Spot("vine")) < 300f);

        /// <summary>Where the game keeps its vines (Items/vine is only a
        /// segment): the vine attack's pieces, which a VineSpawner or a vine
        /// trap (Trigger.prefabToSpawn) makes.</summary>
        private static readonly string[] VinePrefabs =
        {
            "Traps/vineAttack_vines", "Traps/vineAttack_100",
            "Traps/vineAttack_140", "Traps/vineAttack_200",
            "Traps/vineAttack_400", "Traps/vineAttack_background", "Items/vine",
        };

        /// <summary>
        /// The first prefab with a ChainParent: one of VinePrefabs, or what one
        /// of them spawns.
        /// </summary>
        private static GameObject VinePrefab(out string from)
        {
            bool Has(Object o) => o is GameObject g
                && g.GetComponentInChildren<ChainParent>(true) != null;
            foreach (var path in VinePrefabs)
            {
                var pf = Resources.Load("Prefabs/" + path) as GameObject;
                if (pf == null)
                {
                    continue;
                }

                if (Has(pf))
                {
                    from = path;
                    return pf;
                }

                foreach (var t in pf.GetComponentsInChildren<Trigger>(true))
                {
                    foreach (var o in new[]
                    {
                        t.prefabToSpawn,
                        t.nonPooledPrefabToSpawn
                    })
                    {
                        if (Has(o))
                        {
                            from = $"{path} (its trap's {o.name})";
                            return (GameObject)o;
                        }
                    }
                }

                foreach (var vs in pf.GetComponentsInChildren<VineSpawner>(
                    true))
                {
                    foreach (var o in vs.vines)
                    {
                        if (Has(o))
                        {
                            from = $"{path} (its spawner's {o.name})";
                            return o;
                        }
                    }
                }
            }

            from = null;
            return null;
        }

        /// <summary>"vine-make" (both games): a vine at the vine spot, as a
        /// VineSpawner makes one in each game after the join (not shared, found
        /// by where it stands). It isn't fastened to anything, so it's taken as
        /// attached and staying so (no detach timer).</summary>
        private static IEnumerator VineMake(string[] a, Result r)
        {
            var pf = VinePrefab(out string from);
            if (pf == null)
            {
                r.Fail(
                    "no prefab with a ChainParent among "
                        + $"{string.Join(", ", VinePrefabs)}");
                yield break;
            }

            Vector3 position = Spot("vine");
            Quaternion rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform parent = null;
            if (Core.ItemContainer != null)
            {
                parent = Core.ItemContainer.transform;
            }

            var go = Object.Instantiate(pf, position, rotation, parent);
            go.name = pf.name;
            var cp = go.GetComponentInChildren<ChainParent>(true);
            // init runs a frame later
            yield return new WaitForSecondsRealtime(0.5f);
            cp.timeToDetach = 0f;
            cp.attached = true;
            if (cp.health <= 0f)
            {
                cp.health = cp.maxHealth;
            }

            ObjRef vine = ObjRef.Of(cp.gameObject);
            string spot = Fmt(cp.transform.position);
            float health = cp.health;
            float full = cp.maxHealth;
            string segment = "none";
            if (cp.lastChainSegment != null)
            {
                segment = cp.lastChainSegment.name;
            }

            r.Info =
                $"{from}: {vine} at {spot}: health {health:0} of {full:0}, "
                    + $"segment {segment}";
            if (cp.maxHealth <= 0f)
            {
                r.Fail(r.Info + ": no health to cut");
            }
        }

        /// <summary>"vine-cut half|all": our player hits the vine (MeleeSensor
        /// -> ChainParent.getHit) for half its maximum health, or for what's
        /// left.</summary>
        private static IEnumerator VineCut(string[] a, Result r)
        {
            var p = Player.Instance;
            var cp = Vine();
            if (cp == null || !cp.attached)
            {
                r.Fail($"no attached vine ({cp != null})");
                yield break;
            }

            yield return Walk(cp.transform.position, 150f);
            float dmg;
            if (a.Length > 0 && a[0] == "all")
            {
                dmg = cp.health;
            }
            else
            {
                dmg = Mathf.Floor(cp.maxHealth / 2f);
            }

            Vine seg = null;
            if (cp.lastChainSegment != null)
            {
                seg = cp.lastChainSegment.GetComponent<Vine>();
            }

            if (seg == null)
            {
                seg = cp.GetComponentInChildren<Vine>();
            }

            if (seg == null)
            {
                r.Fail("the vine has no segment to hit");
                yield break;
            }

            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() => cp.getHit(dmg, p.transform, seg));
            yield return new WaitForSecondsRealtime(1.5f);
            var refused = Refused(mark);
            r.Info =
                $"hit {dmg:0}: health {cp.health:0}, attached {cp.attached}";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
        }

        /// <summary>
        /// "vine-check attached|cut [HEALTH|half]": the vine here (waiting up
        /// to 5 s).
        /// </summary>
        private static IEnumerator VineCheck(string[] a, Result r)
        {
            bool attached = a.Length == 0 || a[0] == "attached";
            var cp = Vine();
            if (cp == null)
            {
                r.Fail("no vine");
                yield break;
            }

            // "half": what one vine-cut half left
            float health;
            if (a.Length < 2)
            {
                health = -1f;
            }
            else if (a[1] == "half")
            {
                health = cp.maxHealth - Mathf.Floor(cp.maxHealth / 2f);
            }
            else
            {
                health = F(a[1]);
            }

            bool Ok() => cp.attached == attached
                && (health < 0f || Mathf.Abs(cp.health - health) < 0.5f);
            float until = Time.realtimeSinceStartup + 5f;
            while (!Ok() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            r.Info =
                $"{ObjRef.Of(cp.gameObject)}: attached {cp.attached}, "
                    + $"health {cp.health:0} of {cp.maxHealth:0}";
            if (!Ok())
            {
                r.Fail(r.Info);
            }
        }
    }
}
