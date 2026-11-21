using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for furniture placed or made, and its IDs.</summary>
    public static partial class MapScenarioWorld
    {
        /// <summary>"place-furniture DX DZ": place a piece of furniture that
        /// can be broken there (centre + offset), as a placed trap is placed
        /// (shared as ObjSpawn): the first of a few prefabs that is breakable.
        /// Each is first tried unshared, in this game only, and only the one
        /// kept is placed as shared: a tried one that isn't kept is removed
        /// here with Object.Destroy, which no other game hears of, so it
        /// mustn't have been sent (a slow frame let the tried chair's ObjSpawn
        /// out before it was removed, and it stayed in the client's
        /// game).</summary>
        private static IEnumerator PlaceFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            at.y = P.transform.position.y;
            yield return Walk(at, 150f);
            foreach (var prefab in new[]
            {
                "Furnitures/Cottage/chair_1",
                "Furnitures/Cottage/table_1",
                "Furnitures/Cottage/shelf"
            })
            {
                var tried = Core.AddPrefab(
                    prefab, at, Quaternion.Euler(90f, 0f, 0f),
                    Core.ItemContainer, worldSpace: true);
                // its health is set in Start
                yield return null;
                yield return null;
                Item t = null;
                if (tried != null)
                {
                    t = tried.GetComponent<Item>();
                }

                bool breakable = t != null && t.destructible && !t.destroyed
                    && t.health > 1 && !t.hasInventory;
                if (tried != null)
                {
                    Object.Destroy(tried);
                }

                if (!breakable)
                {
                    continue;
                }

                yield return null; // the tried one is gone
                GameObject made;
                Containers.Placing = true;
                try
                {
                    made = Core.AddPrefab(
                        prefab, at, Quaternion.Euler(90f, 0f, 0f),
                        Core.ItemContainer, worldSpace: true);
                }
                finally
                {
                    Containers.Placing = false;
                }

                yield return new WaitForSeconds(2f);
                Item it = null;
                if (made != null)
                {
                    it = made.GetComponent<Item>();
                }

                if (it == null)
                {
                    r.Fail($"{prefab}: none placed");
                    yield break;
                }

                r.Info =
                    $"{prefab} "
                        + $"{ObjRef.Of(made)} "
                        + $"at {Fmt(made.transform.position)}, "
                        + $"health {it.health}";
                yield break;
            }

            r.Fail("none of the prefabs is breakable furniture");
        }

        // the chair world-furniture made in this game
        private static Item _worldMade;
        private static int _worldMadeHealth;

        /// <summary>"world-furniture DX DZ" (both): a breakable chair appears
        /// there (centre + offset) as the world makes things by itself (as a
        /// location spawns its characters): in each game on its own, not shared
        /// as ObjSpawn. The host gives it an id (HostIds), and the client's
        /// copy, made within its time window near the spot, is tied to
        /// it.</summary>
        private static IEnumerator WorldFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            at.y = P.transform.position.y;
            yield return Walk(at, 150f);
            var made = Core.AddPrefab(
                "Furnitures/Cottage/chair_1", at, Quaternion.Euler(90f, 0f, 0f),
                Core.ItemContainer, worldSpace: true);
            if (made != null)
            {
                Core.addToSaveable(made, isDynamic: true);
            }

            // its Start, then the host's id and the client's tie
            yield return new WaitForSeconds(3f);
            if (made != null)
            {
                _worldMade = made.GetComponent<Item>();
            }
            else
            {
                _worldMade = null;
            }

            if (_worldMade == null)
            {
                r.Fail("no chair made");
                yield break;
            }

            _worldMadeHealth = _worldMade.health;
            r.Info =
                $"{made.name} "
                    + $"{ObjRef.Of(made)} at {Fmt(made.transform.position)}, "
                    + $"health {_worldMade.health}, ids given {HostIds.Given}, "
                    + $"tied {HostIds.Tied}";
        }

        /// <summary>"ids-tied [hit]" (both): the chair world-furniture made is
        /// named by the host's id (a Spawned ref that finds it here); with
        /// "hit", the host's hit on it came here too.</summary>
        private static IEnumerator IdsTied(string[] a, Result r)
        {
            if (_worldMade == null)
            {
                r.Fail("world-furniture made nothing here");
                yield break;
            }

            bool hit = a.Length > 0 && a[0] == "hit";
            float until = Time.realtimeSinceStartup + 5f;
            while (hit && _worldMade != null
                && _worldMade.health >= _worldMadeHealth
                && !_worldMade.destroyed && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.25f);
            }

            if (_worldMade == null)
            {
                r.Info = "gone";
                if (!hit)
                {
                    r.Fail("the chair is gone");
                }

                yield break;
            }

            var go = _worldMade.gameObject;
            var at = ObjRef.Of(go);
            r.Info =
                $"{go.name} "
                    + $"{at} tied {ObjRef.IsShared(go)}, "
                    + $"health {_worldMadeHealth} -> {_worldMade.health} "
                    + $"broken={_worldMade.destroyed}";
            if (at.K != ObjRef.Kind.Spawned || !ObjRef.IsShared(go)
                || ObjRef.Resolve<Item>(at) != _worldMade)
            {
                r.Fail($"{r.Info}: not named by a host id");
            }
            else if (hit && _worldMade.health >= _worldMadeHealth
                && !_worldMade.destroyed)
            {
                r.Fail($"{r.Info}: the host's hit didn't come here");
            }
        }

        /// <summary>
        /// "ids-hit" (host): our player hits the chair world-furniture made.
        /// </summary>
        private static IEnumerator IdsHit(string[] a, Result r)
        {
            if (_worldMade == null)
            {
                r.Fail("world-furniture made nothing here");
                yield break;
            }

            yield return Walk(_worldMade.transform.position);
            AsPlayer(() => _worldMade.getHit(10, P.transform));
            yield return new WaitForSeconds(1f);
            if (_worldMade == null)
            {
                r.Info = "gone";
            }
            else
            {
                r.Info =
                    $"health {_worldMadeHealth} -> {_worldMade.health} "
                        + $"{ObjRef.Of(_worldMade.gameObject)}";
            }
        }
    }
}
