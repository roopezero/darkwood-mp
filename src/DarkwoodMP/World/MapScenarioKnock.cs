using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for loose things knocked about (DragSync's pushed objects; tools/tests/map_scenario_knock.py).
    /// The map:savage run found four of the save's dropped items that both games had moved 10-20 units, at rest 2-5
    /// units apart. Here a host's enemy, the client's player and the host's player each push one of the dropped items
    /// by the test centre, and the module then compares the dropped items in both games.
    ///
    ///   kn-info [R]          the dropped items within R (250) of the centre: where, their body and colliders, and
    ///                        whether DragSync watches them. The first call notes their order (nearest first), which
    ///                        the other steps' INDEX counts in, so both games pick the same item.
    ///   kn-player INDEX      our player walks straight through that item (and 80 past it), then waits for it to rest.
    ///   kn-enemy INDEX [KIND] (host) a KIND (Dog) is spawned beside that item and driven straight through it, then
    ///                        removed; the client's frozen copy of it passes through the client's copy of the item.
    ///   kn-slide [TYPE]      (host, before the join) TYPE (rope) dropped as the game drops it (spawnDroppedInvItemm:
    ///                        a random push of 100-250 away from the player), and kept sliding to and fro until the
    ///                        world is read for the joiner's snapshot; then pushed once more, so it comes to rest while
    ///                        the joiner is still loading, away from where the snapshot has it. That is what the test
    ///                        world's kit does when it doesn't fit the host's bags and a client joins at once.
    ///
    /// The dropped items are on layer 25, which players' and enemies' bodies don't collide with: in the first run of
    /// this module the dog and both players went through them and none moved. The kn-enemy and kn-player steps stay
    /// to show that.
    /// </summary>
    public static class MapScenarioKnock
    {
        private static Player P => Player.Instance;
        private static readonly List<string> _order = new List<string>();

        public static void Register()
        {
            Add("kn-info", InfoStep, timeout: 10f);
            Add("kn-player", PlayerPush, timeout: 40f);
            Add("kn-enemy", EnemyPush, timeout: 30f);
            Add("kn-slide", Slide, timeout: 10f);
        }

        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0.0} {p.z:0.0}");

        private static IEnumerable<Item> Dropped(float radius) =>
            StateDump.All<Item>().Where(i => i != null && i.gameObject.name.StartsWith("DroppedItem", StringComparison.Ordinal)
                && StateDump.Flat(i.transform.position - Centre) < radius);

        private static string Ref(Item i) => ObjRef.Of(i.gameObject).ToString();

        private static Item ByIndex(string[] a, Result r)
        {
            int k = a.Length > 0 ? int.Parse(a[0], CultureInfo.InvariantCulture) : 0;
            if (k < 0 || k >= _order.Count) { r.Fail($"no item {k} (kn-info found {_order.Count})"); return null; }
            var it = Dropped(1000f).FirstOrDefault(i => Ref(i) == _order[k]);
            if (it == null) r.Fail($"item {k} ({_order[k]}) not here");
            return it;
        }

        private static string Describe(Item i)
        {
            var sb = new StringBuilder();
            var inv = i.GetComponent<Inventory>();
            string type = inv != null && inv.slots.Count > 0 && !InvItemClass.isNull(inv.slots[0].invItem) ? inv.slots[0].invItem.type : "?";
            var b = i.GetComponent<Rigidbody>();
            sb.Append(FormattableString.Invariant($"{Ref(i)} {type} at {i.transform.position.x:0.0} {i.transform.position.y:0.0} {i.transform.position.z:0.0} active={i.gameObject.activeInHierarchy}"));
            if (b == null) sb.Append(" no body");
            else sb.Append(FormattableString.Invariant($" body kin={b.isKinematic} cons={b.constraints} sleep={b.IsSleeping()} v={b.velocity.magnitude:0.0} mass={b.mass:0.##} drag={b.drag:0.##}"));
            foreach (var c in i.GetComponentsInChildren<Collider>(true))
                sb.Append($" col[{c.GetType().Name} {c.gameObject.name} on={c.enabled} trig={c.isTrigger} layer={c.gameObject.layer}]");
            for (var t = i.transform.parent; t != null; t = t.parent)
                sb.Append(FormattableString.Invariant($" in {t.name}@{t.position.x:0.0},{t.position.z:0.0}"));
            if (b != null) sb.Append(FormattableString.Invariant($" bodypos {b.position.x:0.0} {b.position.z:0.0} interp={b.interpolation}"));
            sb.Append(" ").Append(DragSync.Instance != null ? DragSync.Instance.WatchInfo(b) : "no DragSync");
            return sb.ToString();
        }

        private static IEnumerator InfoStep(string[] a, Result r)
        {
            float radius = a.Length > 0 ? float.Parse(a[0], CultureInfo.InvariantCulture) : 250f;
            var list = Dropped(radius).OrderBy(i => StateDump.Flat(i.transform.position - Centre)).ToList();
            if (_order.Count == 0) _order.AddRange(list.Select(Ref));
            foreach (var i in list) Plugin.Log.LogInfo($"[knock] {Describe(i)}");
            r.Info = string.Join("; ", list.Select(i => $"{_order.IndexOf(Ref(i))}={Ref(i)} {Fmt(i.transform.position)}"));
            if (list.Count == 0) r.Fail("no dropped items by the centre");
            yield break;
        }

        private static IEnumerator PlayerPush(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null) yield break;
            var start = it.transform.position;
            var dir = start - P.transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward;
            yield return Walk(start - dir * 90f, 10f, detours: 0);
            yield return Walk(start + dir * 80f, 5f, detours: 0);
            yield return new WaitForSeconds(3f); // at rest, and sent
            r.Info = $"{Ref(it)} from {Fmt(start)} to {Fmt(it.transform.position)} ({StateDump.Flat(it.transform.position - start):0.0} units)";
        }

        private static IEnumerator Slide(string[] a, Result r)
        {
            string type = a.Length > 0 ? a[0] : "rope";
            float since = Time.unscaledTime;
            var t = P.spawnDroppedInvItemm(false, type, 1);
            var b = t != null ? t.GetComponent<Rigidbody>() : null;
            if (b == null) { r.Fail($"couldn't drop {type}"); yield break; }
            Plugin.Instance.StartCoroutine(KeepSliding(b, since));
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{ObjRef.Of(t.gameObject)} {type} sliding from {Fmt(t.position)}";
        }

        private static IEnumerator KeepSliding(Rigidbody b, float since)
        {
            float until = Time.unscaledTime + 120f;
            float side = 1f;
            while (b != null && DragSync.SnapshotAt < since && Time.unscaledTime < until)
            {
                b.WakeUp();
                b.velocity = new Vector3(200f * side, 0f, 0f);
                side = -side;
                yield return new WaitForSeconds(0.4f);
            }
            if (b == null) yield break;
            var at = b.position;
            b.WakeUp();
            b.velocity = new Vector3(0f, 0f, 250f);
            Plugin.Log.LogInfo($"[knock] {ObjRef.Of(b.gameObject)} pushed at {Fmt(at)} after the snapshot (at {DragSync.SnapshotAt:0.0}, now {Time.unscaledTime:0.0})");
            yield return new WaitForSeconds(3f);
            if (b != null) Plugin.Log.LogInfo($"[knock] {ObjRef.Of(b.gameObject)} at rest at {Fmt(b.position)}");
        }

        private static IEnumerator EnemyPush(string[] a, Result r)
        {
            var it = ByIndex(a, r);
            if (it == null) yield break;
            string kind = a.Length > 1 ? a[1] : "Dog";
            var start = it.transform.position;
            var dir = start - P.transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward;
            dir = Quaternion.Euler(0f, 90f, 0f) * dir; // across, not from our player's side
            var from = start - dir * 150f; var to = start + dir * 150f;
            Character c;
            try { c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, from, 1f, kind, nocturnal: false); }
            catch (Exception e) { r.Fail($"spawning {kind}: {e.GetType().Name}: {e.Message}"); yield break; }
            if (c == null) { r.Fail($"couldn't spawn {kind}"); yield break; }
            var rb = c.GetComponent<Rigidbody>();
            c.transform.position = new Vector3(from.x, c.transform.position.y, from.z);
            yield return new WaitForSeconds(1f); // the client's copy is made
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                var at = Vector3.Lerp(from, to, t / 2f);
                at.y = c.transform.position.y;
                if (rb != null) rb.MovePosition(at); else c.transform.position = at;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            Object.Destroy(c.gameObject);
            yield return new WaitForSeconds(3f); // at rest, and sent
            r.Info = $"{kind} through {Ref(it)}: from {Fmt(start)} to {Fmt(it.transform.position)} ({StateDump.Flat(it.transform.position - start):0.0} units)";
        }
    }
}
