using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Sync.Players;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Corpses outside their location, and view-scan.</summary>
    public static partial class MapScenarioLocations
    {
        /// <summary>
        /// "corpse-out" (host): a world location's savage dies outside that
        /// location, beside our player, and then the location is left
        /// (Location.activateCharacters(false), as when nobody is near it any
        /// more). Its corpse must stay where it fell, switched on and
        /// searchable: it used to vanish here while the clients still had it.
        /// </summary>
        private static IEnumerator CorpseOut(string[] a, Result r)
        {
            var p = Player.Instance;
            var loc = Object.FindObjectsOfType<Location>()
                .Where(
                    l => l != null && !l.isOutsideLocation
                        && l.characters != null
                        && !Core.withinLocation(p.transform.position, 0f, l))
                .OrderBy(
                    l => (l.transform.position
                            - p.transform.position).sqrMagnitude)
                .FirstOrDefault();
            if (loc == null)
            {
                r.Fail("no world location with a characters holder");
                yield break;
            }

            string kind = "Villager";
            if (a.Length > 0)
            {
                kind = a[0];
            }

            var c = MapScenarioFixChecks.SpawnAt(
                p.transform.position + new Vector3(300f, 0f, 200f), kind);
            if (c == null)
            {
                r.Fail("couldn't spawn the savage");
                yield break;
            }

            // One of the location's characters that chased someone out of it
            // without being disconnected.
            c.baseLocation = loc;
            c.baseCharacters = loc.characters;
            c.disconnectedFromLocation = false;
            c.transform.SetParent(loc.characters, true);
            if (!loc.charactersList.Contains(c))
            {
                loc.charactersList.Add(c);
            }

            yield return new WaitForSeconds(1f);
            string where =
                $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at "
                    + $"{Fmt(c.transform.position)}, of "
                    + $"{Core.getTrueLocationName(loc.name)} (entered "
                    + $"{loc.entered}), on {c.gameObject.activeInHierarchy}";
            if (!c.gameObject.activeInHierarchy)
            {
                r.Fail($"it's switched off before it died: {where}");
                yield break;
            }

            yield return MapScenarioItems2.KillAsPlayer(c, r, 1f);
            if (c == null || c.alive)
            {
                r.Fail($"it didn't die: {where}");
                yield break;
            }

            // its death animation: the corpse becomes searchable
            yield return new WaitForSeconds(3f);
            int mark = MapScenarioIssues.LogMark();
            loc.activateCharacters(false);
            if (loc.entered)
            {
                loc.activateCharacters(true); // its own characters as they were
            }

            yield return null;
            bool on = c != null && c.gameObject.activeInHierarchy,
                item = c != null && c.GetComponent<Item>() != null;
            r.Info =
                $"{where}; after the location was left: on {on}, searchable "
                    + $"{item}, in its list {loc.charactersList.Contains(c)}";
            if (!on)
            {
                r.Fail(
                    $"its corpse was switched off with the location: {r.Info}");
            }
            else if (!item)
            {
                r.Fail($"its corpse can't be searched: {r.Info}");
            }
            else
            {
                var missing = new List<string>();
                yield return MapScenarioIssues.WaitLog(mark, new[]
                {
                    @"corpse at .* lies outside .*: "
                        + "kept as that location is left"
                }, 3f, missing);

                if (missing.Count > 0)
                {
                    r.Fail($"not in the log: {string.Join(", ", missing)}");
                }
            }
        }

        /// <summary>
        /// "corpse-out-check" (client): the host's savage corpse from
        /// corpse-out is here, shown and searchable.
        /// </summary>
        private static IEnumerator CorpseOutCheck(string[] a, Result r)
        {
            yield return new WaitForSeconds(1f);
            var p = Player.Instance;
            NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

            var c = Object.FindObjectsOfType<Character>()
                .Where(x =>
                {
                    if (x != null && !x.alive)
                    {
                        string name = x.name;
                        string prefix = "Villager";
                        if (a.Length > 0)
                        {
                            prefix = a[0];
                        }

                        return name.StartsWith(prefix) && ne != null
                            && ne.NetIdOf(x, out _);
                    }

                    return false;
                })
                .OrderBy(
                    x => (x.transform.position
                            - p.transform.position).sqrMagnitude)
                .FirstOrDefault();

            if (c == null)
            {
                r.Fail("no corpse of the host's savage here");
                yield break;
            }

            bool item = c.GetComponent<Item>() != null;
            string blockers = string.Join(
                ", ",
                NetEnemies.FovBlockers(c, NetEnemies.FovShadowMask()));
            r.Info =
                $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at "
                    + $"{Fmt(c.transform.position)}, on "
                    + $"{c.gameObject.activeInHierarchy}, searchable {item}, "
                    + "cuts the view "
                    + $"[{blockers}]";
            if (!c.gameObject.activeInHierarchy || !item)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"view-scan [KIND]": see Register.</summary>
        private static IEnumerator ViewScan(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var p = Player.Instance;
            var from = ViewWatch.LightPos();
            if (p == null || from == null)
            {
                r.Fail("no player view light");
                yield break;
            }

            float y = from.Value.y;
            string kind = "Villager";
            if (a.Length > 0)
            {
                kind = a[0];
            }

            var corpse = Object.FindObjectsOfType<Character>().Where(
                x => x != null && !x.alive && x.name.StartsWith(kind))
                .OrderBy(
                    x => (x.transform.position
                            - p.transform.position).sqrMagnitude)
                .FirstOrDefault();
            var centres = new List<(string, Vector3)>
            {
                ("our player", p.transform.position)
            };

            if (corpse != null)
            {
                centres.Add(
                    ($"{corpse.name}'s corpse", corpse.transform.position));
            }

            foreach (var t in Object.FindObjectsOfType<Transform>().Where(
                t => t.parent == null && t.name.StartsWith("DarkwoodMP_")))
            {
                centres.Add((t.name, t.position));
            }

            var hiders =
                new SortedDictionary<string, int>(StringComparer.Ordinal);
            var bad = new List<string>();
            int spots = 0, dark = 0;
            foreach (var (what, c) in centres)
            {
                for (float dx = -60f; dx <= 60f; dx += 3f)
                {
                    for (float dz = -60f; dz <= 60f; dz += 3f)
                    {
                        var at = new Vector3(c.x + dx, y, c.z + dz);
                        spots++;
                        if (!ViewWatch.Hidden(at))
                        {
                            continue;
                        }

                        dark++;
                        foreach (var h in Physics.RaycastAll(
                            at + new Vector3(0f, 100f, 0f), Vector3.down, 200f,
                            ViewWatch.HideMask))
                        {
                            if (h.collider == null)
                            {
                                continue;
                            }

                            var t = h.collider.transform;
                            string path = ViewWatch.Path(t);
                            string text = "";
                            if (h.collider.isTrigger)
                            {
                                text = ", trigger";
                            }

                            string key =
                                $"{path} [layer "
                                    + $"{h.collider.gameObject.layer}{text}]";
                            if (hiders.TryGetValue(key, out int n))
                            {
                                hiders[key] = n + 1;
                            }
                            else
                            {
                                hiders[key] = 1;
                            }

                            bool ours = path.StartsWith("DarkwoodMP_")
                                || t.GetComponentInParent<Character>() != null;
                            if (ours && !bad.Contains(key))
                            {
                                bad.Add(key);
                            }
                        }
                    }
                }

                yield return null;
            }

            string hidden = string.Join(
                "; ",
                hiders.Select(kv => $"{kv.Key} x{kv.Value}"));
            r.Info =
                $"{spots} spots around "
                    + $"{string.Join(", ", centres.Select(x => x.Item1))}: "
                    + $"{dark} with the view off, " +
                     "by "
                    + $"[{hidden}]";
            if (bad.Count > 0)
            {
                r.Fail(
                    "a character, corpse, puppet or stand-in switches our "
                        + $"player's view off: {string.Join("; ", bad)}. "
                        + $"{r.Info}");
            }
        }
    }
}
