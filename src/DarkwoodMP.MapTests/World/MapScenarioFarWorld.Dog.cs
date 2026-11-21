using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Steps for an enemy whose copy culling switched off.</summary>
    public static partial class MapScenarioFarWorld
    {
        // ---- the host's enemy whose copy our culling switched off -----------

        // host: the dog far-dog made
        private static Character _dog;
        // client: our copy of it, as far-dog-shown found it
        private static Character _copy;

        /// <summary>
        /// "far-dog SPOT" (host, before the client joins, so it's in the save
        /// the client loads): a dog there.
        /// </summary>
        private static IEnumerator FarDog(string[] a, Result r)
        {
            string s = "off:0,2400";
            if (a.Length > 0)
            {
                s = a[0];
            }

            var spot = Spot(s);
            // Run again at the far steps: the dog made before the join may have
            // wandered off or died in the meantime, so a living one is put back
            // on its spot and a dead one is made again.
            if (_dog != null && _dog.alive)
            {
                spot.y = _dog.transform.position.y;
                _dog.transform.position = spot;
                var body = _dog.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = spot;
                }

                yield return new WaitForSeconds(1.5f);
                string name = _dog.name;
                string at = Fmt(_dog.transform.position);
                SaveableObject save = _dog.GetComponent<SaveableObject>();
                int? id = null;
                if (save != null)
                {
                    id = save.uniqueId;
                }

                r.Info = $"{name} back at {at}, save id {id}";
                yield break;
            }

            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                P.gameObject, spot, 1f, "Dog", nocturnal: false,
                attackPlayer: false);
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            c.transform.position = spot;
            _dog = c;
            yield return new WaitForSeconds(1.5f);
            string spawnedName = c.name;
            string spawnedAt = Fmt(c.transform.position);
            SaveableObject spawnedSave = c.GetComponent<SaveableObject>();
            int? spawnedId = null;
            if (spawnedSave != null)
            {
                spawnedId = spawnedSave.uniqueId;
            }

            r.Info = $"{spawnedName} at {spawnedAt}, save id {spawnedId}";
        }

        private static Character CopyNear(Vector3 at, float within)
        {
            var ne = NetEnemies.Instance;
            if (ne == null)
            {
                return null;
            }

            return Object.FindObjectsOfType<Character>(true).Where(
                c => c != null && c.alive && ne.IsPuppet(c, out _)
                    && StateDump.Flat(c.transform.position - at) < within)
                .OrderBy(c => StateDump.Flat(c.transform.position - at))
                .FirstOrDefault();
        }

        private static bool Shown(Character c) =>
            c != null && c.gameObject.activeInHierarchy
                && c.GetComponentsInChildren<Renderer>().Any(x => x.enabled);

        private static string Describe(Character c)
        {
            string state =
                $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at "
                    + $"{Fmt(c.transform.position)} "
                    + $"on={c.gameObject.activeInHierarchy} (self "
                    + $"{c.gameObject.activeSelf}) shown={Shown(c)}";
            string origin = "our copy from the save";
            SaveableObject save = c.GetComponent<SaveableObject>();
            if (save != null && save.dontSave)
            {
                origin = "made from its prefab";
            }

            return state + $" {origin}";
        }

        /// <summary>
        /// "far-dog-active" (client): every living copy of the host's enemies
        /// outside our own culling's rect (more than 1700 from us along z or
        /// 2500 along x, so our culling has its node off) is switched on and
        /// shown. The host's dog 2400 north of the centre is one, when the host
        /// is by it; at least one must be found.
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
                far = Object.FindObjectsOfType<Character>(true)
                    .Where(
                        c => c != null && c.alive && ne.IsPuppet(c, out _)
                            && Outside(c)).ToArray();
                if (far.Length > 0 && far.All(Shown))
                {
                    break;
                }

                yield return new WaitForSeconds(0.5f);
            }

            if (far.Length == 0)
            {
                r.Fail(
                    "no copy of the host's enemies outside our culling's rect");
                yield break;
            }

            var hidden = far.Where(c => !Shown(c)).ToArray();
            int total = far.Length;
            int unseen = hidden.Length;
            Character example = hidden.FirstOrDefault();
            if (example == null)
            {
                example = far[0];
            }

            r.Info =
                $"{total} outside our rect, {unseen} not shown; e.g. "
                    + $"{Describe(example)}";
            if (hidden.Length > 0)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "far-dog-to-client" (host): put the dog 250 from the client's player
        /// (its proxy here).
        /// </summary>
        private static IEnumerator FarDogToClient(string[] a, Result r)
        {
            if (_dog == null)
            {
                r.Fail("no dog (far-dog first)");
                yield break;
            }

            var other =
                NetPlayerProxy.All.Values.FirstOrDefault(p => p != null);
            if (other == null)
            {
                r.Fail("no other player here");
                yield break;
            }

            // It goes for a player by itself, and the test world has bear traps
            // on the way (Saved:21368, 21369). A bear trap kills a character
            // outright (Trigger.checkCollision calls die()), so health doesn't
            // save it: it died there twice before the client could look for it.
            // The step is about it being shown, not about the fight, so it's
            // held on its spot until far-dog-gone (or 30 s).
            _dog.maxHealth = 100000f;
            _dog.health = 100000f;
            var to = other.transform.position + new Vector3(250f, 0f, 0f);
            to.y = _dog.transform.position.y;
            _dog.transform.position = to;
            var rb = _dog.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = to;
            }

            Plugin.Instance.StartCoroutine(HoldDog(_dog, to));
            yield return new WaitForSeconds(2f);
            r.Info =
                $"{_dog.name} at {Fmt(_dog.transform.position)}, "
                    + $"on={_dog.gameObject.activeInHierarchy}";
        }

        /// <summary>
        /// Host: keeps the dog on its spot (see far-dog-to-client) while it's
        /// still the test's dog, for up to 30 s.
        /// </summary>
        private static IEnumerator HoldDog(Character dog, Vector3 at)
        {
            for (float t = 0f; t < 30f && dog != null && dog == _dog
                && dog.alive; t += Time.deltaTime)
            {
                dog.transform.position = at;
                var rb = dog.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = at;
                    rb.velocity = Vector3.zero;
                }

                yield return null;
            }
        }

        /// <summary>
        /// "far-dog-shown" (client): the host's dog, now beside us, is shown.
        /// </summary>
        private static IEnumerator FarDogShown(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 10f
                && !Shown(c = CopyNear(P.transform.position, 500f)); t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
            }

            if (c == null)
            {
                r.Fail("no copy of the host's enemy within 500");
                yield break;
            }

            _copy = c;
            r.Info = Describe(c);
            if (!Shown(c))
            {
                r.Fail($"{r.Info}: beside us and not shown");
            }
        }

        /// <summary>
        /// "far-dog-gone" (host): the dog removed (the client is told to
        /// despawn its copy).
        /// </summary>
        private static IEnumerator FarDogGone(string[] a, Result r)
        {
            if (_dog == null)
            {
                r.Fail("no dog");
                yield break;
            }

            r.Info = $"{_dog.name} removed";
            var go = _dog.gameObject;
            try
            {
                _dog.removeMe();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[map] removeMe: {e.Message}");
            }

            if (go != null)
            {
                Object.Destroy(go);
            }

            _dog = null;
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>
        /// "far-dog-hidden" (client): our copy is no longer shown, and no
        /// longer the host's.
        /// </summary>
        private static IEnumerator FarDogHidden(string[] a, Result r)
        {
            if (_copy == null)
            {
                r.Info = "the copy was made from its prefab and is gone";
                yield break;
            }

            for (float t = 0f; t < 10f
                && (Shown(_copy)
                    || NetEnemies.Instance.IsPuppet(_copy, out _)); t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (_copy == null)
                {
                    r.Info = "the copy was made from its prefab and is gone";
                    yield break;
                }
            }

            r.Info = Describe(_copy);
            if (Shown(_copy) || NetEnemies.Instance.IsPuppet(_copy, out _))
            {
                r.Fail($"{r.Info}: still shown");
            }
        }
    }
}
