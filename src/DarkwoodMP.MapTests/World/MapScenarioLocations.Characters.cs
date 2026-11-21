using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>A location's characters: sight, saves, taming, noise.</summary>
    public static partial class MapScenarioLocations
    {
        /// <summary>
        /// What a character's sight check
        /// (Character.checkForCharactersInViewRange, canSeeEnemy) finds of
        /// `who`.
        /// </summary>
        private static string SightOf(Character c, Component who)
        {
            var from = c.transform.position;
            var dir = who.transform.position - from;
            float range = c.farViewDistance * c.aniSightRangeModifier;
            var col = who.GetComponent<Collider>();
            bool listed = col != null && c.charactersInViewRange.Contains(col);
            var hits = Physics.RaycastAll(from, dir, range, 18909185);
            string hitNames = string.Join(
                ", ",
                hits.Select(
                    h => $"{h.collider.name}(layer "
                        + $"{h.collider.gameObject.layer}, {h.distance:0})"));
            string geometry =
                $"{who.name}: {StateDump.Flat(dir):0} away (sight {range:0}), "
                    + $"y {from.y:0} vs {who.transform.position.y:0}, angle "
                    + $"{Vector3.Angle(dir, c.transform.up):0} (view "
                    + $"{c.fieldOfViewRange}), ";
            string collider = "none";
            if (col != null)
            {
                string state = "off";
                if (col.enabled)
                {
                    state = "on";
                }

                collider = state + " layer " + who.gameObject.layer;
            }

            bool sees = Core.canSee(c.transform, who.transform);
            bool inSight = c.charactersInSight.Any(
                s => s != null && s.transform == who.transform);
            return geometry +
                $"in view range list {listed}, collider {collider}, " +
                $"ray hits [{hitNames}], Core.canSee "
                + $"{sees}, in sight "
                + $"{inSight}";
        }

        private static IEnumerator Sight(string[] a, Result r)
        {
            if (_dog == null)
            {
                r.Fail("no dog (loc-dog first)");
                yield break;
            }

            var proxy = Joiner();
            if (proxy == null)
            {
                r.Fail("no joiner in a separate location we run");
                yield break;
            }

            float health = _dog.health;
            float until = Time.realtimeSinceStartup + 8f;
            string first = null;
            while (Time.realtimeSinceStartup < until && _dog != null
                && _dog.target != proxy.transform)
            {
                var dir = proxy.transform.position - _dog.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1f
                    && _dog.behaviour != Character.Behaviour.chasingTarget)
                {
                    // facing them (its up is its front)
                    _dog.transform.rotation =
                        Quaternion.LookRotation(Vector3.down, dir.normalized);
                }

                yield return new WaitForSeconds(0.25f);
                if (first == null)
                {
                    first = SightOf(_dog, proxy);
                }
            }

            if (_dog == null)
            {
                r.Fail("the dog is gone");
                yield break;
            }

            r.Info =
                $"{_dog.name}: {Behaviour(_dog)}; first look: {first}; now: "
                    + $"{SightOf(_dog, proxy)}";
            if (_dog.target != proxy.transform)
            {
                r.Fail($"it doesn't see {proxy.OwnerName}: {r.Info}");
            }
            else if (_dog.health < health)
            {
                r.Fail($"it was hit, so it isn't sight: {r.Info}");
            }
        }

        private static IEnumerator Save(string[] a, Result r)
        {
            // A forced save: only into a copy of the saves (the test world or
            // --dwmp-sandbox), never the player's own.
            if (!SaveRedirect.Active)
            {
                r.Fail(
                    "the saves aren't a test copy (--dwmp-sandbox or the test "
                        + "world)");
                yield break;
            }

            var o = Singleton<OutsideLocations>.Instance;
            var proxy = Joiner();
            if (proxy == null)
            {
                r.Fail("no joiner in a separate location we run");
                yield break;
            }

            var loc = proxy.InLocation;
            if (o.playerInOutsideLocation)
            {
                r.Fail(
                    $"our player is in {Locations.Current()}: this checks a "
                        + "location kept only for the joiner");
                yield break;
            }

            try
            {
                Singleton<SaveManager>.Instance.Save(doJson: true, force: true);
            }
            catch (Exception e)
            {
                r.Fail($"Save threw: {e.Message}");
                yield break;
            }

            yield return new WaitForSeconds(1f);
            // what the save writes for it (SaveManager.SavedObj: lS)
            var ls = new Location.SaveState(loc);
            var os = new OutsideLocations.SaveState(o);
            r.Info =
                $"saved: {loc.name} entered {ls.entered} (live: entered "
                    + $"{loc.entered}, kept {Locations.KeptForOthers(loc)}), " +
                     $"our player in a location {os.playerInOutsideLocation}, "
                    + $"current '{os.currentLocationName}'";
            if (ls.entered)
            {
                r.Fail($"the save says our player went into it: {r.Info}");
            }
            else if (os.playerInOutsideLocation)
            {
                r.Fail($"the save puts our player in a location: {r.Info}");
            }
            else if (!loc.entered || !Locations.KeptForOthers(loc))
            {
                r.Fail($"saving switched it off for the joiner: {r.Info}");
            }
        }

        /// <summary>
        /// Characters within a location's reach of its marker
        /// (Locations.HostRunsAt's 15000), active or not.
        /// </summary>
        private static IEnumerable<Character> LocationCharacters(
            Location loc) =>
            Object.FindObjectsOfType<Character>(true).Where(
                c => NetEnemies.Eligible(c)
                    && StateDump.Flat(
                        c.transform.position - loc.transform.position)
                        < 15000f);

        private static IEnumerator Tame(string[] a, Result r)
        {
            var proxy = Joiner();
            if (proxy == null)
            {
                r.Fail("no joiner in a separate location we run");
                yield break;
            }

            int n = 0;
            foreach (var c in LocationCharacters(proxy.InLocation))
            {
                foreach (var s in c.sensorTypes)
                {
                    if (s != null)
                    {
                        s.damage = 1;
                    }
                }

                n++;
            }

            r.Info = $"{n} characters in {proxy.InLocation.name} bite for 1";
        }

        /// <summary>
        /// Our player put at `at` (at its own height), its rigidbody too.
        /// </summary>
        private static void PutAt(Player p, Vector3 at)
        {
            at.y = p.transform.position.y;
            p.transform.position = at;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = at;
            }
        }

        private static IEnumerator Approach(string[] a, Result r)
        {
            var ne = NetEnemies.Instance;
            var p = Player.Instance;
            var c = Object.FindObjectsOfType<Character>()
                .Where(
                    x => x.alive && x.sleeping && ne != null
                        && ne.IsPuppet(x, out _) && !x.name.StartsWith("Dog"))
                .OrderBy(
                    x => StateDump.Flat(
                        x.transform.position
                            - p.transform.position)).FirstOrDefault();
            if (c == null)
            {
                r.Fail("no sleeping copy of the host's enemies here");
                yield break;
            }

            // Put beside it, not walked there: a walk across the location can
            // cross its way out (see loc-hit).
            // In front of it (a character looks along its transform.up), so it
            // sees us once it wakes. The location is turned at random: from the
            // side we came in, a sleeper could have its back to us, and wake
            // only to listen.
            var side = c.transform.up;
            side.y = 0f;
            if (side.sqrMagnitude < 0.01f)
            {
                side = p.transform.position - c.transform.position;
                side.y = 0f;
            }

            Vector3 heading;
            if (side.sqrMagnitude > 1e-4f)
            {
                heading = side.normalized;
            }
            else
            {
                heading = Vector3.right;
            }

            var at = c.transform.position + heading * 70f;
            PutAt(p, at);
            yield return new WaitForSeconds(0.3f);
            OutsideLocations instance = Singleton<OutsideLocations>.Instance;
            bool? playerInOutsideLocation = null;
            if (instance != null)
            {
                playerInOutsideLocation = instance.playerInOutsideLocation;
            }

            bool stillOutside = false;
            if (playerInOutsideLocation.HasValue)
            {
                stillOutside = playerInOutsideLocation.Value;
            }

            if (!stillOutside)
            {
                r.Fail(
                    "our player left the location on the way to the sleeper");
                yield break;
            }

            ne.IsPuppet(c, out uint id);
            float away =
                StateDump.Flat(c.transform.position - p.transform.position);
            r.Info =
                $"by the host's sleeping {c.name} #{id} at "
                    + $"{Fmt(c.transform.position)}, "
                    + $"{away:0} away";
        }

        private static IEnumerator Noise(string[] a, Result r)
        {
            var at = Player.Instance.transform.position;
            var ne = NetEnemies.Instance;
            // Until the host's nearest sleeper (its copy here says it's asleep,
            // from EnemyState) wakes, or 20 steps.
            var sleeper = Object.FindObjectsOfType<Character>()
                .Where(
                    x => x.alive && x.sleeping && ne != null
                        && ne.IsPuppet(x, out _))
                .OrderBy(x => StateDump.Flat(x.transform.position - at))
                .FirstOrDefault();
            int steps = 0;
            for (; steps < 20 && (sleeper == null || sleeper.sleeping); steps++)
            {
                PlayerNoisePatch.OwnNoise++;
                try
                {
                    // a running step (Player.checkFrameTrigger)
                    Character.alertInArea(at, 350f, false, 1f);
                }
                finally
                {
                    PlayerNoisePatch.OwnNoise--;
                }

                yield return new WaitForSeconds(0.3f);
            }

            string spot = Fmt(at);
            string who = "none";
            if (sleeper != null)
            {
                who = sleeper.name;
            }

            r.Info =
                $"ran on the spot at {spot} for {steps} steps; the host's "
                    + $"{who} asleep here: "
                    + $"{(sleeper != null && sleeper.sleeping)}";
        }

        private static IEnumerator Wake(string[] a, Result r)
        {
            var proxy = Joiner();
            if (proxy == null)
            {
                r.Fail("no joiner in a separate location we run");
                yield break;
            }

            var ne = NetEnemies.Instance;
            var c = LocationCharacters(proxy.InLocation)
                .Where(
                    x => x.alive && x.gameObject.activeInHierarchy
                        && !x.name.StartsWith("Dog") && ne.NetIdOf(x, out _))
                .OrderBy(
                    x => StateDump.Flat(
                        x.transform.position
                            - proxy.transform.position)).FirstOrDefault();
            if (c == null)
            {
                r.Fail($"no enemy of {proxy.InLocation.name} here");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until
                && (c.sleeping || c.target != proxy.transform))
            {
                yield return new WaitForSeconds(0.25f);
            }

            yield return new WaitForSeconds(1.5f); // it comes at them
            ne.NetIdOf(c, out uint id);
            float away = StateDump.Flat(
                c.transform.position - proxy.transform.position);
            r.Info =
                $"{c.name} #{id} asleep {c.sleeping}, {Behaviour(c)}, "
                    + $"{away:0} from {proxy.OwnerName}, " +
                     $"threat {Threat.Of(c).Get(proxy):0.#}";
            _woken = c;
            if (c.sleeping || c.target != proxy.transform)
            {
                r.Fail(
                    $"it didn't wake and go for {proxy.OwnerName}: {r.Info}");
            }
        }

        private static IEnumerator Stopped(string[] a, Result r)
        {
            var o = Singleton<OutsideLocations>.Instance;
            Location loc = null;
            if (a.Length > 0)
            {
                loc = o.getLocation(a[0]);
            }

            if (loc == null)
            {
                r.Fail("no such location here");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 8f;
            int on = 0;
            while (Time.realtimeSinceStartup < until)
            {
                on = LocationCharacters(loc).Count(
                    c => c.alive && c.gameObject.activeInHierarchy
                        && c.enabled);
                if (!Locations.KeptForOthers(loc) && !loc.entered && on == 0
                    && Joiner() == null)
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            int dead = LocationCharacters(loc).Count(c => !c.alive);
            r.Info =
                $"{loc.name}: kept {Locations.KeptForOthers(loc)}, entered "
                    + $"{loc.entered}, characters running {on}, dead ones "
                    + $"{dead}, " +
                     $"a joiner in a location we run: {Joiner() != null}";
            if (Locations.KeptForOthers(loc) || loc.entered || on > 0
                || Joiner() != null)
            {
                r.Fail($"it still runs: {r.Info}");
            }
        }
    }
}
