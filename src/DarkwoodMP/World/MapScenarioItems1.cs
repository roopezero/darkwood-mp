using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for items used on the world: a trap placed the game's own way (Player.progressBarCompleted)
    /// and the host's checks on what a client places, doors opened with a key (the host checks the journal), and
    /// the porter whistle (the host calls the porter in every game). The list is tools/tests/map_scenario_items1.py.
    /// </summary>
    public static class MapScenarioItems1
    {
        private static Player P => Player.Instance;
        private static Session S => Session.Instance;
        private static readonly System.Reflection.MethodInfo ShowProxyItem = AccessTools.Method(typeof(Player), "showProxyItem");

        public static void Register()
        {
            Add("trap-place-far", TrapPlaceFar, timeout: 30f);
            Add("place-forged", PlaceForged);
            Add("door-lock-key", DoorLockKey);
            Add("door-key", DoorKey, timeout: 40f); // the walk back from trap-place-far (1400 away) took 10 to 19 s, then up to 8 s for the refusal
            Add("door-locked", DoorLocked);
            Add("porter-ready", PorterReady);
            Add("whistle", Whistle, timeout: 40f);
            Add("porter-came", PorterCame, timeout: 40f);
            Add("porter-forged", PorterForged);
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");

        // ---- placing ------------------------------------------------------------------------------------------

        /// <summary>The item type whose placed prefab (InvItem.item) is named so ("beartrap"), or null.</summary>
        private static string PlaceableType(string prefab)
        {
            var db = Singleton<ItemsDatabase>.Instance;
            foreach (var kv in db.itemsDict.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var item = (Resources.Load(kv.Value) as GameObject)?.GetComponent<InvItem>();
                if (item != null && item.canBePlaced && item.item != null && item.item.name == prefab) return kv.Key;
            }
            return null;
        }

        /// <summary>TYPE in a hotbar slot, that slot selected and in our hands (as MapScenarioIssues' "hold").</summary>
        private static IEnumerator HoldItem(string type, Result r)
        {
            var p = P;
            int slot = p.Hotbar.slots.FindIndex(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type);
            if (slot < 0)
            {
                slot = p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
                if (slot < 0) { slot = 0; p.Hotbar.slots[0].removeItem(); }
                p.Hotbar.slots[slot].inventory = p.Hotbar;
                p.Hotbar.slots[slot].createItem(type, 1);
            }
            if (p.Hotbar.slots[slot].selected) p.selectCurrentHotbarSlot();
            else p.Hotbar.selectSlot(slot, noiseless: true, force: true);
            float giveUp = Time.realtimeSinceStartup + 8f;
            while ((p.switchingItem || InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) && Time.realtimeSinceStartup < giveUp)
                yield return null;
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type)
                r.Fail($"couldn't take {type} in our hands (holding {(InvItemClass.isNull(p.currentItem) ? "nothing" : p.currentItem.type)})");
        }

        /// <summary>
        /// Our player places PREFAB at `at` the game's own way: the item in our hands, its proxy (Player.showProxyItem)
        /// moved there, and Player.progressBarCompleted with placingItem, as the placing progress bar ends. made gets
        /// what was placed and shared (a new ObjRef.Spawned entry), or null.
        /// </summary>
        internal static IEnumerator PlaceReal(string prefab, Vector3 at, Result r, Action<GameObject> made)
        {
            string type = PlaceableType(prefab);
            if (type == null) { r.Fail($"no item places {prefab}"); yield break; }
            yield return HoldItem(type, r);
            if (r.Failure != null) yield break;
            var p = P;
            var before = new HashSet<long>(ObjRef.Spawned.Keys);
            AsPlayer(() =>
            {
                ShowProxyItem.Invoke(p, new object[] { p.currentItem.baseClass.item });
                p.proxyItem.transform.localPosition = at;
                p.placingItem = true;
                try { p.progressBarCompleted(); }
                finally { p.placingItem = false; }
            });
            var id = ObjRef.Spawned.Keys.FirstOrDefault(k => !before.Contains(k));
            made(id != 0 && ObjRef.Spawned.TryGetValue(id, out var go) ? go : null);
        }

        /// <summary>
        /// "trap-place-far PREFAB" (client): place one the game's way 1500 from our player. The host refuses it (out
        /// of reach): ours goes again and the item comes back.
        /// </summary>
        private static IEnumerator TrapPlaceFar(string[] a, Result r)
        {
            string prefab = a.Length > 0 ? a[0] : "beartrap";
            string type = PlaceableType(prefab);
            if (type == null) { r.Fail($"no item places {prefab}"); yield break; }
            yield return HoldItem(type, r);
            if (r.Failure != null) yield break;
            int had = MapScenarioWorld.CarriedOf(type), refused = Authority.RolledBack;
            var at = P.transform.position + new Vector3(1500f, 0f, 0f);
            GameObject go = null;
            yield return PlaceReal(prefab, at, r, g => go = g);
            if (r.Failure != null) yield break;
            if (go == null) { r.Fail("placed, but it isn't shared (no ObjRef.Spawned entry)"); yield break; }
            int after = MapScenarioWorld.CarriedOf(type);
            float giveUp = Time.realtimeSinceStartup + 8f;
            while (Authority.RolledBack == refused && Time.realtimeSinceStartup < giveUp) yield return null;
            yield return new WaitForSeconds(0.5f);
            int back = MapScenarioWorld.CarriedOf(type);
            r.Info = $"{prefab} at {Fmt(at)}: carried {had} -> {after} -> {back}, ours {(go == null ? "removed" : "still there")}";
            if (Authority.RolledBack == refused) r.Fail($"{r.Info}: the host didn't refuse it");
            else if (go != null) r.Fail($"{r.Info}: refused, but ours is still there");
            else if (back != had) r.Fail($"{r.Info}: the item didn't come back");
        }

        /// <summary>
        /// "place-forged PREFAB DX DZ [--none]" (client): send the host a placed ObjSpawn our game didn't make (centre +
        /// offset): one out of reach, or a prefab no player can place. --none: in reach, but our player carries nothing
        /// that places it (all of it taken away, and our character sent, as placing does first). The host must refuse it.
        /// </summary>
        private static IEnumerator PlaceForged(string[] a, Result r)
        {
            if (S == null || !S.IsClient) { r.Fail("a client's step"); yield break; }
            if (a.Contains("--none"))
            {
                string path = a[0];
                var types = Containers.PlacedWith(path);
                if (types == null) { r.Fail($"no item places {path}"); yield break; }
                foreach (var type in types)
                {
                    int n = MapScenarioWorld.CarriedOf(type);
                    if (n > 0) P.Inventory.removeItemAmountFromPlayer(type, n);
                    for (int i = 0; i < P.Hotbar.slots.Count; i++)
                        if (!InvItemClass.isNull(P.Hotbar.slots[i].invItem) && P.Hotbar.slots[i].invItem.type == type) P.Hotbar.slots[i].removeItem();
                }
                yield return null;
                int left = types.Sum(t => MapScenarioWorld.CarriedOf(t));
                if (left > 0) { r.Fail($"still carrying {left} that place {path}"); yield break; }
                Characters.Instance?.SaveNow();
            }
            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            at.y = P.transform.position.y;
            var w = S.Begin(MsgType.ObjSpawn);
            w.W.Write(((long)UnityEngine.Random.Range(1, int.MaxValue) << 31) | 12345L);
            w.W.Write((byte)1);
            w.WriteStr(a[0]);
            w.Write(at);
            var rot = Quaternion.Euler(90f, 0f, 0f);
            w.W.Write(rot.x); w.W.Write(rot.y); w.W.Write(rot.z); w.W.Write(rot.w);
            w.WriteBigStr("");
            w.W.Write(0);
            bool? ok = null; string why = null;
            Authority.Request(w, $"forged {a[0]}", (o, reason, res) => { ok = o; why = reason; }, inventory: false);
            float giveUp = Time.realtimeSinceStartup + 8f;
            while (ok == null && Time.realtimeSinceStartup < giveUp) yield return null;
            r.Info = $"{a[0]} at {Fmt(at)}: " + (ok == null ? "no answer" : ok.Value ? "allowed" : "refused: " + why);
            if (ok != false) r.Fail($"{r.Info}: the host didn't refuse it");
            else if (a.Contains("--none") && (why == null || !why.Contains("carry"))) r.Fail($"{r.Info}: refused, but not for what we carry");
        }

        // ---- keys ---------------------------------------------------------------------------------------------

        /// <summary>"door-lock-key N" (host): lock door N with a key our journal hasn't got (the first by name).</summary>
        private static IEnumerator DoorLockKey(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            if (d == null) { r.Fail($"no door {a[0]}"); yield break; }
            var j = Singleton<UI>.Instance.journal;
            string key = Singleton<JournalDatabase>.Instance.keysDict.Keys.Where(k => !string.IsNullOrEmpty(k) && !j.keysDict.ContainsKey(k))
                .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            if (key == null) { r.Fail("no key our journal hasn't got"); yield break; }
            if (d.opened) { d.close(P.transform); yield return new WaitForSeconds(0.5f); }
            d.lockMe(key);
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} locked={l != null && l.locked} key {key}";
            if (l == null || !l.locked) r.Fail($"{r.Info}: not locked");
        }

        /// <summary>
        /// "door-key N [--forged]" (client): open door N's lock with its key, as the player does: the key picked up
        /// (into the journal) and Locked.tryToOpenWithKey in the same frame. --forged: without the key, a key unlock
        /// sent anyway (LockShare), which the host must refuse.
        /// </summary>
        private static IEnumerator DoorKey(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            var l = d != null ? d.GetComponent<Locked>() : null;
            if (l == null || !l.locked) { r.Fail($"door {a[0]} isn't locked here"); yield break; }
            yield return Walk(d.transform.position);
            var j = Singleton<UI>.Instance.journal;
            int refused = Authority.RolledBack;
            if (a.Contains("--forged"))
            {
                if (j.keysDict.ContainsKey(l.keyType)) { r.Fail($"our journal has {l.keyType} already"); yield break; }
                AsPlayer(() => LockShare.Send(l, 1f));
                float giveUp = Time.realtimeSinceStartup + 8f;
                while (Authority.RolledBack == refused && Time.realtimeSinceStartup < giveUp) yield return null;
                yield return new WaitForSeconds(0.5f);
                r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} key {l.keyType}: locked={l.locked}";
                if (Authority.RolledBack == refused) r.Fail($"{r.Info}: the host didn't refuse it");
                else if (!l.locked) r.Fail($"{r.Info}: refused, but unlocked here");
                yield break;
            }
            j.keysDict[l.keyType] = new Journal.Key { type = l.keyType }; // as KeyReference.pickup
            bool opened = false;
            AsPlayer(() => opened = l.tryToOpenWithKey());
            yield return new WaitForSeconds(2f); // a refusal would lock it again
            r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} key {l.keyType}: opened={opened} locked={l.locked}";
            if (!opened || l.locked) r.Fail($"{r.Info}: still locked");
            else if (Authority.RolledBack != refused) r.Fail($"{r.Info}: the host refused it");
        }

        /// <summary>"door-locked N yes|no": door N's lock as expected here.</summary>
        private static IEnumerator DoorLocked(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            var l = d != null ? d.GetComponent<Locked>() : null;
            bool want = a.Length > 1 && a[1] == "yes";
            float giveUp = Time.realtimeSinceStartup + 5f;
            while ((l != null && l.locked) != want && Time.realtimeSinceStartup < giveUp) yield return null;
            r.Info = d == null ? "no such door" : $"{d.name} {MapScenarioWorld.Ref(d)} locked={l != null && l.locked}";
            if ((l != null && l.locked) != want) r.Fail($"{r.Info}, not {(want ? "locked" : "open")}");
        }

        // ---- the porter whistle -------------------------------------------------------------------------------

        /// <summary>The nearest big location to the centre where the porter can come.</summary>
        private static Location PorterPlace() =>
            Object.FindObjectsOfType<Location>().Where(l => l != null && l.porterPosition != null && !l.isSubLocation)
                .OrderBy(l => StateDump.Flat(l.porterPosition.transform.position - Centre)).FirstOrDefault();

        private static bool PorterThere(Location loc) =>
            loc.porter != null || Object.FindObjectsOfType<PorterSpawner>().Any(s => s != null && StateDump.Flat(s.transform.position - loc.porterPosition.transform.position) < 50f);

        /// <summary>"porter-ready" (host): daytime, the porter neither on the way nor there, so a whistle calls it.</summary>
        private static IEnumerator PorterReady(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null) { r.Fail("no place for the porter near the test area"); yield break; }
            var flags = Singleton<Flags>.Instance;
            if (!Core.isDay()) TestWorld.SetTime(12f * 60f);
            if (flags.isFlagTrue("porter_inTransit")) flags.setFlag("porter_inTransit", activeModifier: false);
            if (loc.porter != null) { Object.Destroy(loc.porter); loc.porter = null; }
            yield return new WaitForSeconds(1f);
            r.Info = $"{loc.name} porter at {Fmt(loc.porterPosition.transform.position)}; day={Core.isDay()} killed={flags.isFlagTrue("porter_killed")}";
            if (!Core.isDay() || flags.isFlagTrue("porter_killed")) r.Fail($"{r.Info}: the porter can't come");
        }

        /// <summary>"whistle" (client): our player blows the porter whistle at the porter's place; the host calls it, here too.</summary>
        private static IEnumerator Whistle(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null) { r.Fail("no place for the porter near the test area"); yield break; }
            yield return Walk(loc.porterPosition.transform.position, 150f);
            P.whereAmI.checkWhereAmI();
            if (P.whereAmI.bigLocation != loc) { r.Fail($"we're in {P.whereAmI.bigLocation?.name ?? "no big location"}, not {loc.name}"); yield break; }
            if (PorterThere(loc)) { r.Fail("the porter is there already"); yield break; }
            int refused = Authority.RolledBack;
            AsPlayer(() => new InvItemClass("porterWhistle", 1f, 1).use());
            float giveUp = Time.realtimeSinceStartup + 10f;
            while (!PorterThere(loc) && Authority.RolledBack == refused && Time.realtimeSinceStartup < giveUp) yield return null;
            r.Info = $"at {loc.name}: porter {(loc.porter != null ? "here" : PorterThere(loc) ? "on its way" : "not called")}";
            if (Authority.RolledBack != refused) r.Fail($"{r.Info}: the host refused the call");
            else if (!PorterThere(loc)) r.Fail(r.Info);
        }

        /// <summary>"porter-came": the porter (or its spawner, which waits until our player looks away) is at the place.</summary>
        private static IEnumerator PorterCame(string[] a, Result r)
        {
            var loc = PorterPlace();
            if (loc == null) { r.Fail("no place for the porter near the test area"); yield break; }
            float giveUp = Time.realtimeSinceStartup + 10f;
            while (!PorterThere(loc) && Time.realtimeSinceStartup < giveUp) yield return null;
            r.Info = $"at {loc.name}: porter {(loc.porter != null ? "here" : PorterThere(loc) ? "on its way" : "not called")}";
            if (!PorterThere(loc)) r.Fail(r.Info);
        }

        /// <summary>"porter-forged" (client): ask for the porter at another place for it, where our player isn't. Refused.</summary>
        private static IEnumerator PorterForged(string[] a, Result r)
        {
            var near = PorterPlace();
            var far = Object.FindObjectsOfType<Location>().Where(l => l != null && l.porterPosition != null && !l.isSubLocation && l != near)
                .OrderByDescending(l => StateDump.Flat(l.porterPosition.transform.position - P.transform.position)).FirstOrDefault();
            if (far == null) { r.Fail("only one place for the porter"); yield break; }
            var w = S.Begin(MsgType.PorterCalled);
            w.Write(far.porterPosition.transform.position);
            bool? ok = null; string why = null;
            Authority.Request(w, $"forged porter call at {far.name}", (o, reason, res) => { ok = o; why = reason; }, inventory: false);
            float giveUp = Time.realtimeSinceStartup + 8f;
            while (ok == null && Time.realtimeSinceStartup < giveUp) yield return null;
            r.Info = $"at {far.name}: " + (ok == null ? "no answer" : ok.Value ? "allowed" : "refused: " + why);
            if (ok != false) r.Fail($"{r.Info}: the host didn't refuse it");
        }
    }
}
