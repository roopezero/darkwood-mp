using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using DarkwoodMP.World.Places;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>The host's dog beside the client in a location.</summary>
    public static partial class MapScenarioLocations
    {
        private static IEnumerator Dog(string[] a, Result r)
        {
            var proxy = Joiner();
            float until = Time.realtimeSinceStartup + 8f;
            while (proxy == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
                proxy = Joiner();
            }

            if (proxy == null)
            {
                r.Fail("no joiner in a separate location we run");
                yield break;
            }

            // 250 towards the location's middle: the joiner arrives by its way
            // out, and the location is turned at random, so a fixed direction
            // could put the dog past the exit, and walking to it would leave
            // the location.
            var inward =
                proxy.InLocation.transform.position - proxy.transform.position;
            inward.y = 0f;
            Vector3 heading;
            if (inward.sqrMagnitude > 1f)
            {
                heading = inward.normalized;
            }
            else
            {
                heading = Vector3.right;
            }

            var at = proxy.transform.position + heading * 250f;
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                proxy.gameObject, at, 1f, "Dog", nocturnal: false,
                attackPlayer: false);
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            c.transform.position = at;
            if (proxy.InLocation.waypoints != null)
            {
                // not our player's place's
                c.setWaypoints(proxy.InLocation.waypoints);
            }

            foreach (var s in c.sensorTypes)
            {
                if (s != null)
                {
                    // it goes for the joiner, but the test mustn't kill them
                    s.damage = 1;
                }
            }

            _dog = c;
            yield return new WaitForSeconds(1.5f);
            Coop.Current.Enemies.NetIdOf(c, out uint id);
            float away = StateDump.Flat(
                c.transform.position - proxy.transform.position);
            r.Info =
                $"{c.name} #{id} at {Fmt(c.transform.position)} in "
                    + $"{proxy.InLocation.name}, "
                    + $"{away:0} from {proxy.OwnerName}; " +
                     $"{Behaviour(c)}";
        }

        private static string Behaviour(Character c)
        {
            Character.Behaviour behaviour = c.behaviour;
            bool active = c.isActive;
            bool enabled = c.enabled;
            float health = c.health;
            string target = "nobody";
            if (c.target != null)
            {
                target = c.target.name;
            }

            return $"behaviour {behaviour}, active {active}, enabled "
                + $"{enabled}, health {health:0}, going for {target}";
        }

        /// <summary>
        /// "loc-hit" (client): our player walks up to the host's dog and hits
        /// it (sent to the host as our hit).
        /// </summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

            var p = Player.Instance;
            var dog = Object.FindObjectsOfType<Character>()
                .Where(
                    c => c.alive && c.name.StartsWith("Dog") && ne != null
                        && ne.IsPuppet(c, out _))
                .OrderBy(
                    c => StateDump.Flat(
                        c.transform.position
                            - p.transform.position)).FirstOrDefault();
            if (dog == null)
            {
                r.Fail("no copy of the host's dog here");
                yield break;
            }

            // Put our player beside it rather than walking there: the dog moves
            // (it may have gone for us after loc-sight), and a walk across the
            // location can cross its way out (returnToWorld), which ends every
            // step after.
            var side = p.transform.position - dog.transform.position;
            side.y = 0f;
            Vector3 heading;
            if (side.sqrMagnitude > 1f)
            {
                heading = side.normalized;
            }
            else
            {
                heading = Vector3.right;
            }

            var at = dog.transform.position + heading * 80f;
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
                r.Fail("our player left the location on the way to the dog");
                yield break;
            }

            AsPlayer(() => dog.getHit(5f, p.transform, false, true, false));
            yield return new WaitForSeconds(0.5f);
            ne.IsPuppet(dog, out uint id);
            float away =
                StateDump.Flat(dog.transform.position - p.transform.position);
            r.Info =
                $"hit the host's {dog.name} #{id} for 5 from "
                    + $"{away:0} away";
        }

        /// <summary>
        /// "loc-dog-target" (host): the dog goes for the joiner who hit it (by
        /// threat).
        /// </summary>
        private static IEnumerator DogTarget(string[] a, Result r)
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

            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && _dog != null
                && _dog.target != proxy.transform)
            {
                yield return new WaitForSeconds(0.25f);
            }

            float threat = Plugin.Instance.Host.Threats.Of(_dog).Get(proxy);
            r.Info =
                $"{_dog.name}: {Behaviour(_dog)}, threat from "
                    + $"{proxy.OwnerName}: {threat:0.#}";
            if (_dog.target != proxy.transform)
            {
                r.Fail($"it doesn't go for {proxy.OwnerName}: {r.Info}");
            }
        }

        private static IEnumerator DogCheck(string[] a, Result r)
        {
            bool wantAlive = a.Length == 0 || a[0] != "dead";
            NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

            var me = Player.Instance.transform.position;
            Character dog = null;
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until)
            {
                dog = Object.FindObjectsOfType<Character>()
                    .Where(
                        c => c.name.StartsWith("Dog") && ne != null
                            && ne.NetIdOf(c, out _)
                            && StateDump.Flat(c.transform.position - me)
                                < 2000f)
                    .OrderBy(c => StateDump.Flat(c.transform.position - me))
                    .FirstOrDefault();
                if (dog != null && dog.alive == wantAlive)
                {
                    break;
                }

                yield return new WaitForSeconds(0.3f);
            }

            int ownActive = Object.FindObjectsOfType<Character>().Count(
                c => NetEnemies.Eligible(c) && c.enabled
                    && !Coop.Current.Enemies.Frozen.Contains(c)
                    && StateDump.Flat(c.transform.position - me) < 3000f);
            if (dog == null)
            {
                r.Fail(
                    "no copy of the host's dog near our player in "
                        + $"{Whereabouts.Current()}");
                yield break;
            }

            ne.NetIdOf(dog, out uint id);
            r.Info =
                $"the host's {dog.name} #{id} at "
                    + $"{Fmt(dog.transform.position)}, alive {dog.alive}, "
                    + $"{StateDump.Flat(dog.transform.position - me):0} from "
                    + "us; " +
                     $"characters of our own running here: {ownActive}";
            if (dog.alive != wantAlive)
            {
                string text = "dead";
                if (wantAlive)
                {
                    text = "alive";
                }

                r.Fail($"it should be {text}: {r.Info}");
            }
            else if (ownActive > 0)
            {
                r.Fail(
                    "our own characters run in a location the host runs: "
                        + $"{r.Info}");
            }
        }

        private static IEnumerator Kill(string[] a, Result r)
        {
            bool woken = a.Length > 0 && a[0] == "woken";
            Character c;
            if (woken)
            {
                c = _woken;
            }
            else
            {
                c = _dog;
            }

            if (c == null)
            {
                string why = "no dog (loc-dog first)";
                if (woken)
                {
                    why = "nothing woken (loc-wake first)";
                }

                r.Fail(why);
                yield break;
            }

            yield return MapScenarioItems2.KillAsPlayer(c, r, 1f);
        }
    }
}
