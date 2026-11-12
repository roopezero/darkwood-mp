using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for players far apart (tools/tests/map_scenario_far.py): things done where
    /// the other game has the area switched off by its own culling.
    ///
    /// Spots are offsets from the centre, given as "off:DX,DZ" (or "centre"), so both games pick the same door or
    /// object: the one nearest that spot, inactive ones too (FindObjectsOfType(true)).
    /// </summary>
    public static class MapScenarioFarWorld
    {
        private static Player P => Player.Instance;
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        public static void Register()
        {
            // Walking far: 600/s, so 7000 takes about 12 s, and 90 s is plenty.
            MapScenario.Add("far-goto", FarGoto, timeout: 90f);
            // Item 3: a door opened where the other game has it switched off.
            MapScenario.Add("far-door", FarDoor, timeout: 60f);
            MapScenario.Add("far-door-check", FarDoorCheck, timeout: 60f);
            MapScenario.Add("far-doors-info", FarDoorsInfo);
            // Item 5: an object dragged where the other game has it switched off.
            MapScenario.Add("far-drag", FarDrag, timeout: 60f);
            MapScenario.Add("far-obj-shown", FarObjShown, timeout: 60f);
            // Item 1: the host's enemy whose copy the client's culling switched off.
            MapScenario.Add("far-dog", FarDog);
            MapScenario.Add("far-dog-active", FarDogActive, timeout: 30f);
            MapScenario.Add("far-dog-to-client", FarDogToClient);
            MapScenario.Add("far-dog-shown", FarDogShown, timeout: 30f);
            MapScenario.Add("far-dog-gone", FarDogGone);
            MapScenario.Add("far-dog-hidden", FarDogHidden, timeout: 30f);
        }

        /// <summary>"off:DX,DZ" (from the centre) or "centre".</summary>
        private static Vector3 Spot(string s)
        {
            if (s == null || s == "centre") return Centre;
            if (s.StartsWith("off:"))
            {
                var p = s.Substring(4).Split(',');
                return Centre + new Vector3(F(p[0]), 0f, F(p[1]));
            }
            throw new ArgumentException($"not a spot: {s} (off:DX,DZ or centre)");
        }

        private static bool _locationsLogged;

        /// <summary>"far-goto SPOT": walk there.</summary>
        private static IEnumerator FarGoto(string[] a, Result r)
        {
            if (!_locationsLogged)
            {
                _locationsLogged = true;
                foreach (var l in Object.FindObjectsOfType<Location>(true).Where(l => l != null && !l.isSubLocation))
                    Plugin.Log.LogInfo($"[map] location {Core.getTrueLocationName(l.name)} at {Fmt(l.transform.position)} ({Fmt(l.transform.position - Centre)} from the centre)");
            }
            var to = Spot(a.Length > 0 ? a[0] : "centre");
            yield return Walk(to, 60f);
            r.Info = $"at {Fmt(P.transform.position)}";
        }

        private static Door DoorAt(Vector3 at) =>
            Nearest(Object.FindObjectsOfType<Door>(true).Where(d => !d.destroyed && ObjRef.Of(d.gameObject).Valid), at);

        /// <summary>"far-door SPOT open|close": the door nearest the spot, walked to and opened or closed as our player.</summary>
        private static IEnumerator FarDoor(string[] a, Result r)
        {
            var door = DoorAt(Spot(a[0]));
            if (door == null) { r.Fail("no door"); yield break; }
            yield return Walk(door.transform.position);
            bool open = a.Length < 2 || a[1] == "open";
            if (open != door.opened)
                AsPlayer(() => { if (open) door.open(P.transform.position, P.transform, 0f); else door.close(P.transform); });
            yield return new WaitForSeconds(1f);
            r.Info = $"{door.name} {ObjRef.Of(door.gameObject)} at {Fmt(door.transform.position)} open={door.opened} leaf {DoorLeaf.Angle(door):0} degrees";
            if (door.opened != open) r.Fail($"{r.Info}: not {(open ? "open" : "shut")}");
        }

        /// <summary>
        /// "far-door-check SPOT open|close": walk to the door nearest the spot; once it's switched on here, its leaf
        /// must be drawn as it is (open: turned from shut).
        /// </summary>
        private static IEnumerator FarDoorCheck(string[] a, Result r)
        {
            var door = DoorAt(Spot(a[0]));
            if (door == null) { r.Fail("no door"); yield break; }
            bool open = a.Length < 2 || a[1] == "open";
            yield return Walk(door.transform.position);
            for (float t = 0f; t < 5f && (!door.gameObject.activeInHierarchy || DoorLeaf.Turned(door) != open); t += 0.25f)
                yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f); // let the swing end
            r.Info = $"{door.name} {ObjRef.Of(door.gameObject)} open={door.opened} on={door.gameObject.activeInHierarchy} leaf {DoorLeaf.Angle(door):0} degrees";
            if (door.opened != open || DoorLeaf.Turned(door) != open) r.Fail($"{r.Info}: should be {(open ? "open and drawn open" : "shut")}");
        }

        /// <summary>"far-doors-info SPOT": each door within 1000 of the spot: open, drawn open, its leaf's angle, switched on.</summary>
        private static IEnumerator FarDoorsInfo(string[] a, Result r)
        {
            var at = Spot(a.Length > 0 ? a[0] : "centre");
            r.Info = string.Join("; ", Object.FindObjectsOfType<Door>(true).Where(d => StateDump.Flat(d.transform.position - at) < 1000f)
                .Select(d => $"{ObjRef.Of(d.gameObject)} at {Fmt(d.transform.position)} open={d.opened} leaf {DoorLeaf.Angle(d):0} (z {d.body?.localEulerAngles.z:0}) on={d.gameObject.activeInHierarchy}"));
            yield break;
        }

        /// <summary>
        /// "far-drag FROM TO": the draggable object nearest FROM dragged to TO (both spots) by our player, the way
        /// MapScenarioWorld's drag does it, over a few seconds.
        /// </summary>
        private static IEnumerator FarDrag(string[] a, Result r)
        {
            var from = Spot(a[0]); var to = Spot(a[1]);
            // One on the culling grid (a node of its own), not one switched on and off with its location's objects.
            var it = Nearest(Object.FindObjectsOfType<Item>(true).Where(i => i.draggable && i.GetComponent<Rigidbody>() != null
                && i.GetComponent<CullableObject>()?.node != null
                && ObjRef.Of(i.gameObject).Valid && StateDump.Flat(i.transform.position - from) < 2500f), from);
            if (it == null) { r.Fail($"nothing draggable within 2500 of {Fmt(from)}"); yield break; }
            yield return Walk(it.transform.position, 80f);
            var start = it.transform.position;
            var by = new Vector3(to.x - start.x, 0f, to.z - start.z);
            var rb = it.GetComponent<Rigidbody>();
            P.itemBeingDragged = it;
            const float secs = 4f;
            for (float t = 0f; t < secs; t += Time.deltaTime)
            {
                var at = start + by * (t / secs);
                it.transform.position = at;
                if (rb != null) rb.position = at;
                P.transform.position = new Vector3(at.x, P.transform.position.y, at.z) - by.normalized * 70f;
                yield return null;
            }
            it.transform.position = start + by;
            if (rb != null) { rb.position = start + by; rb.velocity = Vector3.zero; }
            yield return new WaitForSeconds(0.3f);
            P.itemBeingDragged = null;
            yield return new WaitForSeconds(2.5f); // where it came to rest goes out once settled
            r.Info = $"{it.name} {ObjRef.Of(it.gameObject)} from {Fmt(start)} to {Fmt(it.transform.position)}";
            if (StateDump.Flat(it.transform.position - to) > 300f) r.Fail($"{r.Info}: it didn't get to {Fmt(to)}");
        }

        /// <summary>"far-obj-shown SPOT": walk to the spot; the draggable object nearest it must be there and switched on.</summary>
        private static IEnumerator FarObjShown(string[] a, Result r)
        {
            var at = Spot(a[0]);
            yield return Walk(at, 150f);
            yield return new WaitForSeconds(3f); // the culling enters a node a frame, every half second
            var it = Nearest(Object.FindObjectsOfType<Item>(true).Where(i => i.draggable && i.GetComponent<Rigidbody>() != null), at);
            if (it == null) { r.Fail("nothing draggable"); yield break; }
            var co = it.GetComponent<CullableObject>();
            var node = co != null ? co.node : null;
            r.Info = $"{it.name} {ObjRef.Of(it.gameObject)} at {Fmt(it.transform.position)} on={it.gameObject.activeInHierarchy}"
                     + (node != null ? $" node {node.position.x:0},{node.position.y:0} entered={node.entered}" : " no node");
            if (StateDump.Flat(it.transform.position - at) > 300f) r.Fail($"{r.Info}: not at {Fmt(at)}");
            else if (!it.gameObject.activeInHierarchy) r.Fail($"{r.Info}: switched off where we stand");
        }

        // ---- the host's enemy whose copy our culling switched off --------------------------------------------------

        private static Character _dog;     // host: the dog far-dog made
        private static Character _copy;    // client: our copy of it, as far-dog-shown found it

        /// <summary>"far-dog SPOT" (host, before the client joins, so it's in the save the client loads): a dog there.</summary>
        private static IEnumerator FarDog(string[] a, Result r)
        {
            var spot = Spot(a.Length > 0 ? a[0] : "off:0,2400");
            // Again at the far steps: the dog made before the join wanders off in the half hour of the full map test
            // before them (it was found dead by the hideout), so a living one is put back on its spot, a dead one made anew.
            if (_dog != null && _dog.alive)
            {
                spot.y = _dog.transform.position.y;
                _dog.transform.position = spot;
                var body = _dog.GetComponent<Rigidbody>(); if (body != null) body.position = spot;
                yield return new WaitForSeconds(1.5f);
                r.Info = $"{_dog.name} back at {Fmt(_dog.transform.position)}, save id {_dog.GetComponent<SaveableObject>()?.uniqueId}";
                yield break;
            }
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, "Dog", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            c.transform.position = spot;
            _dog = c;
            yield return new WaitForSeconds(1.5f);
            r.Info = $"{c.name} at {Fmt(c.transform.position)}, save id {c.GetComponent<SaveableObject>()?.uniqueId}";
        }

        private static Character CopyNear(Vector3 at, float within)
        {
            var ne = NetEnemies.Instance;
            if (ne == null) return null;
            return Object.FindObjectsOfType<Character>(true).Where(c => c != null && c.alive && ne.IsPuppet(c, out _)
                && StateDump.Flat(c.transform.position - at) < within).OrderBy(c => StateDump.Flat(c.transform.position - at)).FirstOrDefault();
        }

        private static bool Shown(Character c) =>
            c != null && c.gameObject.activeInHierarchy && c.GetComponentsInChildren<Renderer>().Any(x => x.enabled);

        private static string Describe(Character c)
        {
            uint id = 0; NetEnemies.Instance?.NetIdOf(c, out id);
            return $"{c.name} #{id} at {Fmt(c.transform.position)} on={c.gameObject.activeInHierarchy} (self {c.gameObject.activeSelf}) shown={Shown(c)}"
                   + $" {(c.GetComponent<SaveableObject>()?.dontSave == true ? "made from its prefab" : "our copy from the save")}";
        }

        /// <summary>
        /// "far-dog-active" (client): every living copy of the host's enemies outside our own culling's rect (more
        /// than 1700 from us along z or 2500 along x, so our culling has its node off) is switched on and shown. The
        /// host's dog 2400 north of the centre is one, when the host is by it; at least one must be found.
        /// </summary>
        private static IEnumerator FarDogActive(string[] a, Result r)
        {
            var ne = NetEnemies.Instance;
            bool Outside(Character c)
            {
                var d = c.transform.position - P.transform.position;
                return Mathf.Abs(d.z) > 1700f || Mathf.Abs(d.x) > 2500f;
            }
            Character[] far = new Character[0];
            for (float t = 0f; t < 15f; t += 0.5f)
            {
                far = Object.FindObjectsOfType<Character>(true).Where(c => c != null && c.alive && ne.IsPuppet(c, out _) && Outside(c)).ToArray();
                if (far.Length > 0 && far.All(Shown)) break;
                yield return new WaitForSeconds(0.5f);
            }
            if (far.Length == 0) { r.Fail("no copy of the host's enemies outside our culling's rect"); yield break; }
            var hidden = far.Where(c => !Shown(c)).ToArray();
            r.Info = $"{far.Length} outside our rect, {hidden.Length} not shown; e.g. {Describe(hidden.FirstOrDefault() ?? far[0])}";
            if (hidden.Length > 0) r.Fail(r.Info);
        }

        /// <summary>"far-dog-to-client" (host): put the dog 250 from the client's player (its proxy here).</summary>
        private static IEnumerator FarDogToClient(string[] a, Result r)
        {
            if (_dog == null) { r.Fail("no dog (far-dog first)"); yield break; }
            var other = NetPlayerProxy.All.Values.FirstOrDefault(p => p != null);
            if (other == null) { r.Fail("no other player here"); yield break; }
            // It goes for a player by itself, and the test world has bear traps on the way (Saved:21368, 21369). A bear
            // trap kills a character outright (Trigger.checkCollision calls die()), so health doesn't save it: it died
            // there twice before the client could look for it. The step is about it being shown, not about the fight,
            // so it's held on its spot until far-dog-gone (or 30 s).
            _dog.maxHealth = 100000f; _dog.health = 100000f;
            var to = other.transform.position + new Vector3(250f, 0f, 0f);
            to.y = _dog.transform.position.y;
            _dog.transform.position = to;
            var rb = _dog.GetComponent<Rigidbody>(); if (rb != null) rb.position = to;
            Plugin.Instance.StartCoroutine(HoldDog(_dog, to));
            yield return new WaitForSeconds(2f);
            r.Info = $"{_dog.name} at {Fmt(_dog.transform.position)}, on={_dog.gameObject.activeInHierarchy}";
        }

        /// <summary>Host: keeps the dog on its spot (see far-dog-to-client) while it's still the test's dog, for up to 30 s.</summary>
        private static IEnumerator HoldDog(Character dog, Vector3 at)
        {
            for (float t = 0f; t < 30f && dog != null && dog == _dog && dog.alive; t += Time.deltaTime)
            {
                dog.transform.position = at;
                var rb = dog.GetComponent<Rigidbody>(); if (rb != null) { rb.position = at; rb.velocity = Vector3.zero; }
                yield return null;
            }
        }

        /// <summary>"far-dog-shown" (client): the host's dog, now beside us, is shown.</summary>
        private static IEnumerator FarDogShown(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 10f && !Shown(c = CopyNear(P.transform.position, 500f)); t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (c == null) { r.Fail("no copy of the host's enemy within 500"); yield break; }
            _copy = c;
            r.Info = Describe(c);
            if (!Shown(c)) r.Fail($"{r.Info}: beside us and not shown");
        }

        /// <summary>"far-dog-gone" (host): the dog removed (the client is told to despawn its copy).</summary>
        private static IEnumerator FarDogGone(string[] a, Result r)
        {
            if (_dog == null) { r.Fail("no dog"); yield break; }
            r.Info = $"{_dog.name} removed";
            var go = _dog.gameObject;
            try { _dog.removeMe(); } catch (Exception e) { Plugin.Log.LogWarning($"[map] removeMe: {e.Message}"); }
            if (go != null) Object.Destroy(go);
            _dog = null;
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>"far-dog-hidden" (client): our copy is no longer shown, and no longer the host's.</summary>
        private static IEnumerator FarDogHidden(string[] a, Result r)
        {
            if (_copy == null) { r.Info = "the copy was made from its prefab and is gone"; yield break; }
            for (float t = 0f; t < 10f && (Shown(_copy) || NetEnemies.Instance.IsPuppet(_copy, out _)); t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (_copy == null) { r.Info = "the copy was made from its prefab and is gone"; yield break; }
            }
            r.Info = Describe(_copy);
            if (Shown(_copy) || NetEnemies.Instance.IsPuppet(_copy, out _)) r.Fail($"{r.Info}: still shown");
        }
    }
}
