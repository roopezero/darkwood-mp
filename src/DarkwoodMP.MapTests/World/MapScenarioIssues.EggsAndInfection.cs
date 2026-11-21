using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Issues #5 and #6: larva eggs, infection.</summary>
    public static partial class MapScenarioIssues
    {
        // ---- #5 larva eggs, #6 infection: the extra ones --------------------

        /// <summary>
        /// "issue-setup" (host, before the join): a second egg near the area
        /// and a third far off, and two more infection patches.
        /// </summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            var made = new List<string>();
            foreach (var kv in Spots)
            {
                string prefab = "Traps/infection_splat";
                if (kv.Key.StartsWith("egg"))
                {
                    prefab = "Objects/larva_egg_big";
                }

                var go = Place(prefab, Centre + kv.Value);
                if (go == null)
                {
                    r.Fail($"couldn't place {prefab}");
                    yield break;
                }

                made.Add(
                    $"{kv.Key} "
                        + $"{ObjRef.Of(go)} at {Fmt(go.transform.position)}");
            }

            yield return new WaitForSeconds(0.5f);
            r.Info = string.Join("; ", made);
        }

        /// <summary>
        /// "egg-break station|near|far": our player hits that egg until it
        /// breaks (walking to it first).
        /// </summary>
        private static IEnumerator EggBreak(string[] a, Result r)
        {
            var p = Player.Instance;
            string which = "station";
            if (a.Length > 0)
            {
                which = a[0];
            }

            var egg = Egg(which);
            if (egg == null)
            {
                r.Fail("no egg");
                yield break;
            }

            var back = p.transform.position;
            yield return Walk(egg.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            bool wasActive = egg.gameObject.activeInHierarchy;
            for (int i = 0; i < 10 && !egg.destroyed; i++)
            {
                AsPlayer(() => egg.getHit(2, p.transform, true));
                yield return new WaitForSeconds(0.3f);
            }

            r.Info =
                $"{which} egg {egg.gameObject.name} "
                    + $"{ObjRef.Of(egg.gameObject)} "
                    + $"at {Fmt(egg.transform.position)}: "
                    + $"broken {egg.destroyed}, health {egg.health}";
            if (!egg.destroyed)
            {
                r.Fail(r.Info);
                yield break;
            }

            yield return new WaitForSeconds(1f);
            if (StateDump.Flat(back - p.transform.position) > 1000f)
            {
                yield return Walk(back, 100f);
            }

            if (!wasActive)
            {
                r.Info += " (it was culled when we got there)";
            }
        }

        /// <summary>"egg-check station,near,far [--from-other WHICH,...]":
        /// those eggs are broken here, and for each in --from-other the log
        /// says the other game's break arrived (and whether it was culled here
        /// then).</summary>
        private static IEnumerator EggCheck(string[] a, Result r)
        {
            string spec = "station";
            if (a.Length > 0)
            {
                spec = a[0];
            }

            var which = spec.Split(',');
            string[] fromOther;
            if (a.Length > 2 && a[1] == "--from-other")
            {
                fromOther = a[2].Split(',');
            }
            else
            {
                fromOther = new string[0];
            }

            var eggs = which.ToDictionary(w => w, Egg);
            var missing = new List<string>();
            var pats = fromOther.Where(eggs.ContainsKey).Where(
                w => eggs[w] != null)
                .Select(
                    w => @"\[world\] egg "
                        + Regex.Escape(ObjRef.Of(eggs[w].gameObject).ToString())
                        + @" destroyed \(from ").ToArray();
            yield return WaitLog(0, pats, 6f, missing);
            var lines = LogLines();
            var parts = new List<string>();
            foreach (var kv in eggs)
            {
                var e = kv.Value;
                if (e == null)
                {
                    r.Fail($"no {kv.Key} egg here");
                    continue;
                }

                string rf = ObjRef.Of(e.gameObject).ToString();
                var line = lines.LastOrDefault(
                    l => l.Contains($"[world] egg {rf} destroyed"));
                string lineNote = "";
                if (line != null)
                {
                    int at = line.IndexOf("[world]", StringComparison.Ordinal);
                    lineNote = $" '{line.Substring(at)}'";
                }

                parts.Add(
                    $"{kv.Key} {rf} broken {e.destroyed} active "
                        + $"{e.gameObject.activeInHierarchy}" + lineNote);
                if (!e.destroyed)
                {
                    r.Fail($"the {kv.Key} egg {rf} isn't broken here");
                }
            }

            r.Info = string.Join("; ", parts);
            if (missing.Count > 0)
            {
                r.Fail(
                    $"not in the log: {string.Join(", ", missing)}; {r.Info}");
            }
            else if (r.Failure != null)
            {
                r.Failure += "; " + r.Info;
            }
        }

        // ---- #6 infection ---------------------------------------------------

        private static readonly Dictionary<string, Vector3> _noted =
            new Dictionary<string, Vector3>();
        internal static IEnumerator Nothing()
        {
            yield break;
        }

        /// <summary>
        /// "burn-infection" (host): set the station's patch alight as fire does
        /// (a Burn on it, which clears every patch within 40).
        /// </summary>
        private static IEnumerator BurnInfection(string[] a, Result r)
        {
            var inf = Patch("station");
            if (inf == null)
            {
                r.Fail("no infection");
                yield break;
            }

            yield return Walk(inf.transform.position, 150f);
            var near = Object.FindObjectsOfType<Infection>()
                .Where(
                    i => !i.disappearing
                        && StateDump.Flat(
                            i.transform.position - inf.transform.position)
                            <= 40f).ToList();
            AsPlayer(() => inf.gameObject.AddComponent<Burn>().burnTime = 5f);
            yield return new WaitForSeconds(1f);
            int cleared = near.Count(i => i == null || i.disappearing);
            r.Info =
                $"{inf.name} "
                    + $"{ObjRef.Of(inf.gameObject)} "
                    + $"at {Fmt(inf.transform.position)}: "
                    + $"{cleared} of {near.Count} patches cleared";
            if (near.Any(i => i != null && !i.disappearing))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"swing-infection" (client): our player holds a torch and
        /// swings it at the extra patch (MeleeSensor's flaming weapon clears
        /// it: Infection.disappear), as a request to the host.</summary>
        private static IEnumerator SwingInfection(string[] a, Result r)
        {
            var p = Player.Instance;
            var cameFrom = p.transform.position;
            string type = "torch";
            if (a.Length > 0)
            {
                type = a[0];
            }

            var inf = Patch("infection:client");
            if (inf == null)
            {
                r.Fail("no patch");
                yield break;
            }

            string rf = ObjRef.Of(inf.gameObject).ToString(),
                infAt = Fmt(inf.transform.position);
            var free =
                p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0)
            {
                free = 0;
            }

            p.Hotbar.slots[free].inventory = p.Hotbar;
            if (!InvItemClass.isNull(p.Hotbar.slots[free].invItem))
            {
                p.Hotbar.slots[free].removeItem();
            }

            p.Hotbar.slots[free].createItem(type, 1);
            // As Inventory.selectSlot does, but also when that slot is the
            // selected one already (it then does nothing).
            foreach (var s in p.Hotbar.slots)
            {
                s.deselect();
            }

            p.Hotbar.slots[free].select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != type
                    || p.switchingItem) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                r.Fail(
                    $"couldn't take the {type} out (holding "
                        + $"{ItemName(p.currentItem)})");
                yield break;
            }

            var sensor = p.currentItem.baseClass.meleeSensor;
            bool flaming = sensor != null && sensor is GameObject g
                && g.GetComponent<MeleeSensor>() is MeleeSensor ms
                && ms.flaming;
            int mark = LogMark();
            string seen = "";
            // MeleeSensor hits a patch only if Core.canSeeAttack passes: rays
            // from our player at a character's height (14) towards the patch
            // (at y -40), as long as the flat distance, must hit the patch's
            // collider. The sensor's check is against the collider it touched,
            // which may be on a child of the patch.
            Collider hitCollider = inf.GetComponentInChildren<Collider>();
            Transform hitT = null;
            if (hitCollider != null)
            {
                hitT = hitCollider.transform;
            }

            if (hitT == null)
            {
                hitT = inf.transform;
            }

            // The patch's collider is a ball of radius 70 around a point 50
            // above the patch itself, and a ray that starts inside a collider
            // doesn't hit it: so from closer than ~70 it never sees the patch.
            // Stand just outside it, where the torch still reaches into it.
            float[] stand = { 120f, 105f, 95f, 85f, 78f, 72f };
            var tried = new List<string>();
            for (int i = 0; i < 6 && !inf.disappearing; i++)
            {
                foreach (var d in stand)
                {
                    var off = p.transform.position - inf.transform.position;
                    off.y = 0f;
                    if (off.sqrMagnitude < 1f)
                    {
                        off = Vector3.right;
                    }

                    var spot = inf.transform.position + off.normalized * d;
                    spot.y = p.transform.position.y;
                    yield return Walk(spot, 4f);
                    bool sees = Core.canSeeAttack(p.transform, hitT);
                    if (i == 0)
                    {
                        float gap = StateDump.Flat(
                            p.transform.position - inf.transform.position);
                        string sight = "no";
                        if (sees)
                        {
                            sight = "sees";
                        }

                        tried.Add($"{gap:0}:{sight}");
                    }

                    if (sees)
                    {
                        break;
                    }
                }

                if (i == 0)
                {
                    // What the game's ray (Core.canSeeAttack's first) runs into
                    // from here.
                    var from = new Vector3(
                        p.transform.position.x,
                        Core.getYpos(PosType.character, false),
                        p.transform.position.z);
                    var to = new Vector3(
                        hitT.position.x, hitT.position.y, hitT.position.z);
                    float len = StateDump.Flat(to - from);
                    var hits = Physics.RaycastAll(
                        from, (to - from).normalized, len, 44165635);
                    var cc = hitT.GetComponent<Collider>();
                    Vector3? center = null;
                    if (cc != null)
                    {
                        center = cc.bounds.center;
                    }

                    Vector3? extents = null;
                    if (cc != null)
                    {
                        extents = cc.bounds.extents;
                    }

                    bool triggers = Physics.queriesHitTriggers;
                    string names = string.Join(
                        ",",
                        hits.Select(h => $"{h.collider.name} L"
                            + $"{h.collider.gameObject.layer}"));
                    string text =
                        FormattableString.Invariant(
                            $"queriesHitTriggers {triggers}, ")
                        + FormattableString.Invariant(
                            $"player y {p.transform.position.y:0.0}, ")
                        + FormattableString.Invariant(
                            $"ray y {from.y:0.0}, patch y {to.y:0.0} ")
                        + FormattableString.Invariant(
                            $"(inf {inf.transform.position.y:0.0}), ")
                        + FormattableString.Invariant(
                            $"collider {hitT.name} bounds {center} ")
                        + FormattableString.Invariant(
                            $"ext {extents}, ray {len:0.0} hits [{names}]");
                    tried.Add(text);
                }

                Swing(p, inf.transform.position);
                yield return new WaitForFixedUpdate();
                var sensors = Object.FindObjectsOfType<MeleeSensor>()
                    .Where(m => m.type == MeleeSensor.MeleeSensorType.player)
                    .ToList();
                var col = inf.GetComponentsInChildren<Collider>(true);
                if (i == 0)
                {
                    var notes = new List<string>();
                    foreach (var m in sensors)
                    {
                        bool ignores = Physics.GetIgnoreLayerCollision(
                            m.gameObject.layer,
                            1);
                        bool overlaps = m.GetComponentsInChildren<Collider>()
                            .Any(mc => col.Any(
                                ic => mc.bounds.Intersects(ic.bounds)));
                        float away = StateDump.Flat(
                            m.transform.position - inf.transform.position);
                        bool sees =
                            Core.canSeeAttack(p.transform, inf.transform);
                        notes.Add(
                            $"{m.name} flaming {m.flaming} layer "
                                + $"{m.gameObject.layer} ignores layer 1: "
                                + $"{ignores}, overlaps {overlaps} "
                                + $"at {away:0}, sees it {sees}");
                    }

                    string sensed = string.Join(",", notes);
                    var shapes = col.Select(
                        c => $"{c.GetType().Name} on {c.enabled} trigger "
                            + $"{c.isTrigger} layer {c.gameObject.layer}");
                    string colliders = string.Join(",", shapes);
                    float apart = StateDump.Flat(
                        p.transform.position - inf.transform.position);
                    seen =
                        $"; melee {p.currentItem.baseClass.isMelee}, "
                            + $"{sensors.Count} sensor(s) " + sensed
                            + "; patch colliders "
                            + $"{colliders}; player at {apart:0}";
                }

                yield return new WaitForSeconds(0.6f);
            }

            yield return new WaitForSeconds(1f);
            // Off the patch, which hurts while we stand in it, the way we came:
            // the straight way to Centre crosses one of the test world's chain
            // traps (Saved:21369), which holds the player there until the step
            // times out.
            yield return Walk(cameFrom, 60f);
            var refused = LogLines().Skip(mark)
                .FirstOrDefault(
                    l => l.Contains("refused") && l.Contains("infection"));
            r.Info =
                $"{type} (sensor flaming {flaming}) at {rf} {infAt}: cleared "
                    + $"{inf == null || inf.disappearing}{seen}; stood at "
                    + $"{string.Join(" ", tried)}";
            if (inf != null && !inf.disappearing)
            {
                r.Fail(r.Info);
            }
            else if (refused != null)
            {
                r.Fail($"{r.Info}; {refused}");
            }
        }

        /// <summary>
        /// "infection-check station[,infection:client] [--from-other]": those
        /// patches are gone here (and the log says the other game's clear
        /// arrived).
        /// </summary>
        private static IEnumerator InfectionCheck(string[] a, Result r)
        {
            string spec = "station";
            if (a.Length > 0)
            {
                spec = a[0];
            }

            var which = spec.Split(',');
            var missing = new List<string>();
            if (a.Contains("--from-other"))
            {
                yield return WaitLog(
                    0, new[] { @"\[world\] infection cleared: [1-9]" }, 6f,
                    missing);
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }

            var parts = new List<string>();
            foreach (var w in which)
            {
                if (!_noted.TryGetValue(w, out var at))
                {
                    r.Fail($"{w}'s patch wasn't noted (infection-note first)");
                    continue;
                }

                var near = StateDump.All<Infection>()
                    .Where(
                        i => !i.disappearing
                            && StateDump.Flat(i.transform.position - at)
                                <= 20f).ToList();
                parts.Add($"{w}: {near.Count} left at {Fmt(at)}");
                if (near.Count > 0)
                {
                    string refs = string.Join(
                        ", ",
                        near.Select(i => ObjRef.Of(i.gameObject)));
                    r.Fail($"{w}'s infection is still here " + $"({refs})");
                }
            }

            var line = LogLines().LastOrDefault(
                l => l.Contains("[world] infection cleared"));
            string item = "no 'infection cleared' line";
            if (line != null)
            {
                item = line.Substring(
                    line.IndexOf("[world]", StringComparison.Ordinal));
            }

            parts.Add(item);
            r.Info = string.Join("; ", parts);
            if (missing.Count > 0)
            {
                r.Fail($"not in the log: {missing[0]}; {r.Info}");
            }
            else if (r.Failure != null)
            {
                r.Failure += "; " + r.Info;
            }
        }
    }
}
