using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for separate locations (the order they run in is
    /// tools/tests/map_scenario_locations.py): a player goes into a location and back, the host puts a dog beside
    /// the client there, and the dumps are taken around the client's player ("where-me") to compare the enemies.
    /// </summary>
    public static class MapScenarioLocations
    {
        private static Character _dog; // host: the dog loc-dog made
        private static Character _woken; // host: the location's own enemy loc-wake woke

        public static void Register()
        {
            // "where-me": where our player stands (the driver dumps both games around it).
            MapScenario.Add("where-me", (a, r) => Info(r, Fmt(Player.Instance != null ? Player.Instance.transform.position : Vector3.zero)));
            // "loc-go NAME": our player goes into that separate location, as its entrance takes them there.
            MapScenario.Add("loc-go", Go, timeout: 60f);
            // "loc-back": our player goes back to the world, where they came from.
            MapScenario.Add("loc-back", Back, timeout: 40f);
            // "loc-dog" (host): a Dog beside the joiner who's in a separate location, and it goes for them.
            MapScenario.Add("loc-dog", Dog, timeout: 25f);
            // "loc-dog-check alive|dead" (client): the host's dog is shown here, near our player, alive or dead.
            MapScenario.Add("loc-dog-check", DogCheck, timeout: 15f);
            // "loc-hit" (client): our player hits the host's dog; "loc-dog-target" (host): the dog goes for them.
            MapScenario.Add("loc-hit", Hit, timeout: 20f);
            MapScenario.Add("loc-dog-target", DogTarget, timeout: 15f);
            // "loc-kill [woken]" (host): our player kills the dog (or the enemy loc-wake woke).
            MapScenario.Add("loc-kill", Kill);
            // "loc-sight" (host): the dog, turned to face the joiner, sees them (goes for them) before anyone hits it.
            MapScenario.Add("loc-sight", Sight, timeout: 15f);
            // "loc-save" (host): the game saves; a location kept only for the joiner is saved as our player left it.
            MapScenario.Add("loc-save", Save, timeout: 30f);
            // "loc-tame" (host): the enemies of the location the joiner is in bite for 1 (the test mustn't kill them).
            MapScenario.Add("loc-tame", Tame);
            // "loc-approach" (client): our player walks up to the host's nearest sleeping enemy here.
            MapScenario.Add("loc-approach", Approach, timeout: 30f);
            // "loc-noise" (client): our player's running steps (noise enemies hear, sent to the host as ours).
            MapScenario.Add("loc-noise", Noise, timeout: 10f);
            // "loc-wake" (host): the location's enemy nearest the joiner wakes and goes for them.
            MapScenario.Add("loc-wake", Wake, timeout: 20f);
            // "loc-stopped NAME" (host): nobody else is in NAME: it isn't kept running, and its characters are off.
            MapScenario.Add("loc-stopped", Stopped, timeout: 15f);
        }

        private static NetPlayerProxy Joiner() =>
            NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && p.InLocation != null && p.Owner != Sync.PlayerSync.DummyId);

        /// <summary>What a character's sight check (Character.checkForCharactersInViewRange, canSeeEnemy) finds of `who`.</summary>
        private static string SightOf(Character c, Component who)
        {
            var from = c.transform.position;
            var dir = who.transform.position - from;
            float range = c.farViewDistance * c.aniSightRangeModifier;
            var col = who.GetComponent<Collider>();
            bool listed = col != null && c.charactersInViewRange.Contains(col);
            var hits = Physics.RaycastAll(from, dir, range, 18909185);
            string hitNames = string.Join(", ", hits.Select(h => $"{h.collider.name}(layer {h.collider.gameObject.layer}, {h.distance:0})"));
            return
                $"{who.name}: {StateDump.Flat(dir):0} away (sight {range:0}), y {from.y:0} vs {who.transform.position.y:0}, angle {Vector3.Angle(dir, c.transform.up):0} (view {c.fieldOfViewRange}), " +
                $"in view range list {listed}, collider {(col != null ? (col.enabled ? "on" : "off") + " layer " + who.gameObject.layer : "none")}, " +
                $"ray hits [{hitNames}], Core.canSee {Core.canSee(c.transform, who.transform)}, in sight {c.charactersInSight.Any(s => s != null && s.transform == who.transform)}";
        }

        private static IEnumerator Sight(string[] a, Result r)
        {
            if (_dog == null) { r.Fail("no dog (loc-dog first)"); yield break; }
            var proxy = Joiner();
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            float health = _dog.health;
            float until = Time.realtimeSinceStartup + 8f;
            string first = null;
            while (Time.realtimeSinceStartup < until && _dog != null && _dog.target != proxy.transform)
            {
                var dir = proxy.transform.position - _dog.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1f && _dog.behaviour != Character.Behaviour.chasingTarget)
                    _dog.transform.rotation = Quaternion.LookRotation(Vector3.down, dir.normalized); // facing them (its up is its front)
                yield return new WaitForSeconds(0.25f);
                if (first == null) first = SightOf(_dog, proxy);
            }
            if (_dog == null) { r.Fail("the dog is gone"); yield break; }
            r.Info = $"{_dog.name}: {Behaviour(_dog)}; first look: {first}; now: {SightOf(_dog, proxy)}";
            if (_dog.target != proxy.transform) r.Fail($"it doesn't see {proxy.OwnerName}: {r.Info}");
            else if (_dog.health < health) r.Fail($"it was hit, so it isn't sight: {r.Info}");
        }

        private static IEnumerator Save(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            var proxy = Joiner();
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            var loc = proxy.InLocation;
            if (o.playerInOutsideLocation) { r.Fail($"our player is in {Locations.Current()}: this checks a location kept only for the joiner"); yield break; }
            try { Singleton<SaveManager>.Instance.Save(doJson: true, force: true); }
            catch (Exception e) { r.Fail($"Save threw: {e.Message}"); yield break; }
            yield return new WaitForSeconds(1f);
            var ls = new Location.SaveState(loc); // what the save writes for it (SaveManager.SavedObj: lS)
            var os = new OutsideLocations.SaveState(o);
            r.Info = $"saved: {loc.name} entered {ls.entered} (live: entered {loc.entered}, kept {Locations.KeptForOthers(loc)}), " +
                     $"our player in a location {os.playerInOutsideLocation}, current '{os.currentLocationName}'";
            if (ls.entered) r.Fail($"the save says our player went into it: {r.Info}");
            else if (os.playerInOutsideLocation) r.Fail($"the save puts our player in a location: {r.Info}");
            else if (!loc.entered || !Locations.KeptForOthers(loc)) r.Fail($"saving switched it off for the joiner: {r.Info}");
        }

        /// <summary>Characters within a location's reach of its marker (Locations.HostRunsAt's 15000), active or not.</summary>
        private static IEnumerable LocationCharacters(Location loc) =>
            Object.FindObjectsOfType<Character>(true).Where(c => NetEnemies.Eligible(c) && StateDump.Flat(c.transform.position - loc.transform.position) < 15000f);

        private static IEnumerator Tame(string[] a, Result r)
        {
            var proxy = Joiner();
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            int n = 0;
            foreach (Character c in LocationCharacters(proxy.InLocation))
            {
                foreach (var s in c.sensorTypes) if (s != null) s.damage = 1;
                n++;
            }
            r.Info = $"{n} characters in {proxy.InLocation.name} bite for 1";
        }

        private static IEnumerator Approach(string[] a, Result r)
        {
            var ne = NetEnemies.Instance;
            var p = Player.Instance;
            var c = Object.FindObjectsOfType<Character>()
                .Where(x => x.alive && x.sleeping && ne != null && ne.IsPuppet(x, out _) && !x.name.StartsWith("Dog"))
                .OrderBy(x => StateDump.Flat(x.transform.position - p.transform.position)).FirstOrDefault();
            if (c == null) { r.Fail("no sleeping copy of the host's enemies here"); yield break; }
            // Put beside it, not walked there: a walk across the location can cross its way out (see loc-hit).
            // In front of it (a character looks along its transform.up), so it sees us once it wakes. The location is
            // turned at random: from the side we came in, a sleeper could have its back to us, and wake only to listen.
            var side = c.transform.up; side.y = 0f;
            if (side.sqrMagnitude < 0.01f) { side = p.transform.position - c.transform.position; side.y = 0f; }
            var at = c.transform.position + (side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right) * 70f;
            at.y = p.transform.position.y;
            p.transform.position = at;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = at;
            yield return new WaitForSeconds(0.3f);
            if (!(Singleton<OutsideLocations>.Instance?.playerInOutsideLocation ?? false)) { r.Fail("our player left the location on the way to the sleeper"); yield break; }
            ne.IsPuppet(c, out uint id);
            r.Info = $"by the host's sleeping {c.name} #{id} at {Fmt(c.transform.position)}, {StateDump.Flat(c.transform.position - p.transform.position):0} away";
        }

        private static IEnumerator Noise(string[] a, Result r)
        {
            var at = Player.Instance.transform.position;
            var ne = NetEnemies.Instance;
            // Until the host's nearest sleeper (its copy here says it's asleep, from EnemyState) wakes, or 20 steps.
            var sleeper = Object.FindObjectsOfType<Character>()
                .Where(x => x.alive && x.sleeping && ne != null && ne.IsPuppet(x, out _))
                .OrderBy(x => StateDump.Flat(x.transform.position - at)).FirstOrDefault();
            int steps = 0;
            for (; steps < 20 && (sleeper == null || sleeper.sleeping); steps++)
            {
                PlayerNoisePatch.OwnNoise++;
                try { Character.alertInArea(at, 350f, false, 1f); } // a running step (Player.checkFrameTrigger)
                finally { PlayerNoisePatch.OwnNoise--; }
                yield return new WaitForSeconds(0.3f);
            }
            r.Info = $"ran on the spot at {Fmt(at)} for {steps} steps; the host's {(sleeper != null ? sleeper.name : "none")} asleep here: {(sleeper != null && sleeper.sleeping)}";
        }

        private static IEnumerator Wake(string[] a, Result r)
        {
            var proxy = Joiner();
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            var ne = NetEnemies.Instance;
            var c = LocationCharacters(proxy.InLocation).Cast<Character>()
                .Where(x => x.alive && x.gameObject.activeInHierarchy && !x.name.StartsWith("Dog") && ne.NetIdOf(x, out _))
                .OrderBy(x => StateDump.Flat(x.transform.position - proxy.transform.position)).FirstOrDefault();
            if (c == null) { r.Fail($"no enemy of {proxy.InLocation.name} here"); yield break; }
            float until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && (c.sleeping || c.target != proxy.transform)) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1.5f); // it comes at them
            ne.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} asleep {c.sleeping}, {Behaviour(c)}, {StateDump.Flat(c.transform.position - proxy.transform.position):0} from {proxy.OwnerName}, " +
                     $"threat {Threat.Of(c).Get(proxy):0.#}";
            _woken = c;
            if (c.sleeping || c.target != proxy.transform) r.Fail($"it didn't wake and go for {proxy.OwnerName}: {r.Info}");
        }

        private static IEnumerator Stopped(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            var loc = a.Length > 0 ? o.getLocation(a[0]) : null;
            if (loc == null) { r.Fail("no such location here"); yield break; }
            float until = Time.realtimeSinceStartup + 8f;
            int on = 0;
            while (Time.realtimeSinceStartup < until)
            {
                on = LocationCharacters(loc).Cast<Character>().Count(c => c.alive && c.gameObject.activeInHierarchy && c.enabled);
                if (!Locations.KeptForOthers(loc) && !loc.entered && on == 0 && Joiner() == null) break;
                yield return new WaitForSeconds(0.25f);
            }
            int dead = LocationCharacters(loc).Cast<Character>().Count(c => !c.alive);
            r.Info = $"{loc.name}: kept {Locations.KeptForOthers(loc)}, entered {loc.entered}, characters running {on}, dead ones {dead}, " +
                     $"a joiner in a location we run: {Joiner() != null}";
            if (Locations.KeptForOthers(loc) || loc.entered || on > 0 || Joiner() != null) r.Fail($"it still runs: {r.Info}");
        }

        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static IEnumerator Info(Result r, string info) { r.Info = info; yield break; }

        private static IEnumerator Go(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null || a.Length < 1) { r.Fail("no OutsideLocations, or no location named"); yield break; }
            string name = a[0];
            if (o.playerInOutsideLocation) { r.Fail($"already in {Locations.Current()}"); yield break; }
            AsPlayer(() => o.prepareLocation(name, null));
            while (!(o.playerInOutsideLocation && !o.loading && Locations.Current() == name)) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(2f); // the black screen fades, the location's objects switch on
            var p = Player.Instance;
            var loc = o.getLocation(name);
            r.Info = $"in {Locations.Current()} at {Fmt(p.transform.position)} (the location at {(loc != null ? Fmt(loc.transform.position) : "?")}), " +
                     $"host runs it here: {Locations.HostRunsHere()}";
        }

        private static IEnumerator Back(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null || !o.playerInOutsideLocation) { r.Fail("not in a separate location"); yield break; }
            string was = Locations.Current();
            AsPlayer(() => o.returnToWorld());
            while (o.playerInOutsideLocation || o.returningToWorld) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(1f);
            r.Info = $"back from {was} at {Fmt(Player.Instance.transform.position)}, in {Singleton<WorldGrid>.Instance?.currentGrid?.name}";
        }

        private static IEnumerator Dog(string[] a, Result r)
        {
            var proxy = NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && p.InLocation != null && p.Owner != Sync.PlayerSync.DummyId);
            float until = Time.realtimeSinceStartup + 8f;
            while (proxy == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
                proxy = NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && p.InLocation != null && p.Owner != Sync.PlayerSync.DummyId);
            }
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            // 250 towards the location's middle: the joiner arrives by its way out, and the location is turned at random,
            // so a fixed direction could put the dog past the exit, and walking to it would leave the location.
            var inward = proxy.InLocation.transform.position - proxy.transform.position;
            inward.y = 0f;
            var at = proxy.transform.position + (inward.sqrMagnitude > 1f ? inward.normalized : Vector3.right) * 250f;
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(proxy.gameObject, at, 1f, "Dog", nocturnal: false, attackPlayer: false);
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            c.transform.position = at;
            if (proxy.InLocation.waypoints != null) c.setWaypoints(proxy.InLocation.waypoints); // not our player's place's
            foreach (var s in c.sensorTypes) if (s != null) s.damage = 1; // it goes for the joiner, but the test mustn't kill them
            _dog = c;
            yield return new WaitForSeconds(1.5f);
            NetEnemies.Instance.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} at {Fmt(c.transform.position)} in {proxy.InLocation.name}, {StateDump.Flat(c.transform.position - proxy.transform.position):0} from {proxy.OwnerName}; " +
                     $"{Behaviour(c)}";
        }

        private static string Behaviour(Character c) =>
            $"behaviour {c.behaviour}, active {c.isActive}, enabled {c.enabled}, health {c.health:0}, going for {(c.target != null ? c.target.name : "nobody")}";

        /// <summary>"loc-hit" (client): our player walks up to the host's dog and hits it (sent to the host as our hit).</summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            var ne = NetEnemies.Instance;
            var p = Player.Instance;
            var dog = Object.FindObjectsOfType<Character>()
                .Where(c => c.alive && c.name.StartsWith("Dog") && ne != null && ne.IsPuppet(c, out _))
                .OrderBy(c => StateDump.Flat(c.transform.position - p.transform.position)).FirstOrDefault();
            if (dog == null) { r.Fail("no copy of the host's dog here"); yield break; }
            // Put our player beside it rather than walking there: the dog moves (it may have gone for us after
            // loc-sight), and a walk across the location can cross its way out (returnToWorld), which ends every step after.
            var side = p.transform.position - dog.transform.position; side.y = 0f;
            var at = dog.transform.position + (side.sqrMagnitude > 1f ? side.normalized : Vector3.right) * 80f;
            at.y = p.transform.position.y;
            p.transform.position = at;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = at;
            yield return new WaitForSeconds(0.3f);
            if (!(Singleton<OutsideLocations>.Instance?.playerInOutsideLocation ?? false)) { r.Fail("our player left the location on the way to the dog"); yield break; }
            AsPlayer(() => dog.getHit(5f, p.transform, false, true, false));
            yield return new WaitForSeconds(0.5f);
            ne.IsPuppet(dog, out uint id);
            r.Info = $"hit the host's {dog.name} #{id} for 5 from {StateDump.Flat(dog.transform.position - p.transform.position):0} away";
        }

        /// <summary>"loc-dog-target" (host): the dog goes for the joiner who hit it (by threat).</summary>
        private static IEnumerator DogTarget(string[] a, Result r)
        {
            if (_dog == null) { r.Fail("no dog (loc-dog first)"); yield break; }
            var proxy = NetPlayerProxy.All.Values.FirstOrDefault(p => p != null && p.InLocation != null && p.Owner != Sync.PlayerSync.DummyId);
            if (proxy == null) { r.Fail("no joiner in a separate location we run"); yield break; }
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && _dog != null && _dog.target != proxy.transform) yield return new WaitForSeconds(0.25f);
            r.Info = $"{_dog.name}: {Behaviour(_dog)}, threat from {proxy.OwnerName}: {Threat.Of(_dog).Get(proxy):0.#}";
            if (_dog.target != proxy.transform) r.Fail($"it doesn't go for {proxy.OwnerName}: {r.Info}");
        }

        private static IEnumerator DogCheck(string[] a, Result r)
        {
            bool wantAlive = a.Length == 0 || a[0] != "dead";
            var ne = NetEnemies.Instance;
            var me = Player.Instance.transform.position;
            Character dog = null;
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until)
            {
                dog = Object.FindObjectsOfType<Character>()
                    .Where(c => c.name.StartsWith("Dog") && ne != null && ne.NetIdOf(c, out _) && StateDump.Flat(c.transform.position - me) < 2000f)
                    .OrderBy(c => StateDump.Flat(c.transform.position - me)).FirstOrDefault();
                if (dog != null && dog.alive == wantAlive) break;
                yield return new WaitForSeconds(0.3f);
            }
            int ownActive = Object.FindObjectsOfType<Character>().Count(c => NetEnemies.Eligible(c) && c.enabled && !NetEnemies.Frozen.Contains(c)
                                                                               && StateDump.Flat(c.transform.position - me) < 3000f);
            if (dog == null) { r.Fail($"no copy of the host's dog near our player in {Locations.Current()}"); yield break; }
            ne.NetIdOf(dog, out uint id);
            r.Info = $"the host's {dog.name} #{id} at {Fmt(dog.transform.position)}, alive {dog.alive}, {StateDump.Flat(dog.transform.position - me):0} from us; " +
                     $"characters of our own running here: {ownActive}";
            if (dog.alive != wantAlive) r.Fail($"it should be {(wantAlive ? "alive" : "dead")}: {r.Info}");
            else if (ownActive > 0) r.Fail($"our own characters run in a location the host runs: {r.Info}");
        }

        private static IEnumerator Kill(string[] a, Result r)
        {
            var p = Player.Instance;
            bool woken = a.Length > 0 && a[0] == "woken";
            var c = woken ? _woken : _dog;
            if (c == null) { r.Fail(woken ? "nothing woken (loc-wake first)" : "no dog (loc-dog first)"); yield break; }
            if (c.alive) AsPlayer(() => c.getHit(c.health + 500f, p.transform, false, true, false));
            yield return new WaitForSeconds(1f);
            r.Info = $"{c.name} alive {c.alive}";
            if (c.alive) r.Fail("it's still alive");
        }
    }
}
