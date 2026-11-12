using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps for the map4 test (tools/tests/map4_scenario.py): players far from the host and from each other.
    /// WorldGrid switches on only the nodes within about (screen width + 500) / zoom of its player
    /// (WorldGrid.refreshNodes), a few thousand units at most. The host's game also switches them on around every
    /// other player (SharedCullingPatch), but a client's game only around its own, so a spot 6000 or more away is
    /// switched off in the other clients' games, and was in the host's until that player came. The world steps
    /// (toggle-door, take-item, ...) pick what's nearest the centre, so "far-spot" and "centre-me" move the centre
    /// to where our player went.
    /// </summary>
    public static class MapScenarioFar
    {
        public static void Register()
        {
            // "centre-me": pick things nearest to where our player stands from now on.
            MapScenario.Add("centre-me", (a, r) =>
            {
                var p = Player.Instance;
                if (p == null) { r.Fail("no player"); return None(); }
                MapScenario.SetCentre(p.transform.position);
                r.Info = Fmt(p.transform.position);
                return None();
            });
            MapScenario.Add("far-spot", FarSpot, timeout: 90f);
        }

        private static IEnumerator None() { yield break; }
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private const float Cluster = 2500f; // far spots this far apart at least, so two players' don't share a house

        /// <summary>"far-spot MIN INDEX": walk to the INDEXth spot at least MIN from the centre (the test area's)
        /// where there is a plain door (saved, not an entrance, locked, padlocked, barricaded or broken) with an unlocked
        /// container holding something within 600 of it, in the outside world's grid; then pick things nearest
        /// to that door. Spots closer than 2500 to an earlier one count as the same spot, so INDEX 0 and 1 are
        /// different places. The same save gives the same spots in every game.</summary>
        private static IEnumerator FarSpot(string[] a, MapScenario.Result r)
        {
            var p = Player.Instance;
            if (p == null) { r.Fail("no player"); yield break; }
            float min = a.Length > 0 ? float.Parse(a[0], CultureInfo.InvariantCulture) : 6000f;
            int index = a.Length > 1 ? int.Parse(a[1]) : 0;
            var from = MapScenario.Centre;
            var grid = Singleton<WorldGrid>.Instance;
            var outside = grid?.grids.FirstOrDefault(g => g.type == WorldGrid.Grid.Type.world) ?? grid?.currentGrid;
            if (outside == null) { r.Fail("no world grid"); yield break; }
            float half = grid.distance / 2f;
            bool InOutside(Vector3 pos) => outside.nodes.Any(n => Mathf.Abs(pos.x - n.position.x) <= half && Mathf.Abs(pos.z - n.position.y) <= half);
            bool Unlocked(Component c) => !(c.GetComponentInParent<Locked>() is Locked l && l.locked);

            var full = StateDump.All<Inventory>().Where(i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench
                && !i.name.StartsWith("DroppedItem") && Unlocked(i) && !i.transform.IsChildOf(p.transform)
                && i.slots.Any(s => !InvItemClass.isNull(s.invItem)) && ObjRef.Of(i.gameObject).Valid).ToList();
            var doors = StateDump.All<Door>()
                .Where(d => !d.destroyed && !d.isEntrance && !d.barricaded && !d.blocked && Unlocked(d)
                    && !(d.GetComponent<Padlock>() is Padlock pl && pl.locked)
                    && ObjRef.Of(d.gameObject).K == ObjRef.Kind.Saved && StateDump.Flat(d.transform.position - from) >= min)
                .Where(d => full.Any(i => StateDump.Flat(i.transform.position - d.transform.position) <= 600f))
                .OrderBy(d => StateDump.Flat(d.transform.position - from)).ThenBy(d => ObjRef.Of(d.gameObject).Id)
                .Where(d => InOutside(d.transform.position))
                .ToList();
            var spots = new List<Door>();
            foreach (var d in doors)
                if (spots.All(s => StateDump.Flat(s.transform.position - d.transform.position) >= Cluster)) spots.Add(d);
            if (index >= spots.Count) { r.Fail($"only {spots.Count} spots {min:0} or more from {Fmt(from)}"); yield break; }
            var door = spots[index];
            var at = door.transform.position;
            MapScenario.SetCentre(at);
            Plugin.Log.LogInfo($"[map] far-spot {min:0} {index}: {door.name} {ObjRef.Of(door.gameObject)} at {Fmt(at)}, {StateDump.Flat(at - from):0} from {Fmt(from)}; walking from {Fmt(p.transform.position)}");
            yield return MapScenario.Walk(at, 150f);
            r.Info = $"{door.name} {ObjRef.Of(door.gameObject)} at {Fmt(at)}, {StateDump.Flat(at - from):0} from {Fmt(from)} (spot {index} of {spots.Count}); we're at {Fmt(p.transform.position)}";
        }
    }
}
