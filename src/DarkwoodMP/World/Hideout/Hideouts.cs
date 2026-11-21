using DarkwoodMP.World.Inventories;
using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Hideout
{
    /// <summary>
    /// Things that happen to the hideouts as a whole, shared between the games:
    /// the porter (Porter), the ovens (Ovens) and a scripted step that moves
    /// things between two world containers (IsWorldMove).
    /// </summary>
    public static class Hideouts
    {
        private static Session S => Plugin.Instance.Host.Session;
        private static bool Debug => WorldActions.Debug;

        /// <summary>
        /// Playing together in a world (also while replaying another game's
        /// change, unlike Coop.Guards.Sharing).
        /// </summary>
        internal static bool Together
        {
            get
            {
                return TogetherRules.Together(TogetherOf(S));
            }
        }

        /// <summary>
        /// What TogetherRules knows of the session and the world.
        /// </summary>
        private static TogetherFacts TogetherOf(Session s)
        {
            if (s == null)
            {
                return default;
            }

            return new TogetherFacts
            {
                HasSession = true,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                IsHost = s.IsHost,
                Peers = s.Peers.Count,
                ClientMode = NetEnemies.ClientMode,
            };
        }

        internal static Location HideoutById(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            foreach (var loc in Object.FindObjectsOfType<Location>())
            {
                if (loc != null
                    && PorterRules.IsHideout(
                        id, loc.hideoutId, loc.isSubLocation))
                {
                    return loc;
                }
            }

            return null;
        }

        // ---- moveInventory between two world containers ---------------------

        /// <summary>
        /// A GameEvent moveInventory step whose source and targets are all
        /// world containers (none is a player's): world state, so the host runs
        /// it and sends what's in each afterwards. One with targets found at
        /// run time (from the location, the parent) stays the player's own
        /// step, as before.
        /// </summary>
        internal static bool IsWorldMove(GameEvent ge)
        {
            return WorldMoveRules.IsWorldMove(FactsOf(ge));
        }

        /// <summary>
        /// What WorldMoveRules knows of a GameEvent step: the only code that
        /// reads the step and its containers for it. The source and targets
        /// are read only for a moveInventory step from a fixed source.
        /// </summary>
        private static WorldMoveFacts FactsOf(GameEvent ge)
        {
            int objects = 0;
            if (ge.locationObjects != null)
            {
                objects = ge.locationObjects.Count;
            }

            var facts = new WorldMoveFacts
            {
                MovesInventory = ge.type == GameEvent.Type.moveInventory,
                HasSource = ge.sourceTransform != null,
                FromParent = ge.getTargetGameObjectsFromParent,
                FromLocation = ge.getTargetFromLocation,
                LocationObjects = objects,
            };

            if (!facts.MovesInventory || !facts.HasSource || facts.FromParent
                || facts.FromLocation || objects > 0)
            {
                return facts;
            }

            var src = ge.sourceTransform.GetComponent<Inventory>();
            if (src == null)
            {
                return facts;
            }

            facts = facts with
            {
                SourceInventory = true,
                SourcePlayers = IsPlayers(src),
            };

            if (facts.SourcePlayers)
            {
                return facts;
            }

            int n = 0;
            bool players = false;
            foreach (var go in Targets(ge))
            {
                var inv = go.GetComponent<Inventory>();
                if (inv == null)
                {
                    continue;
                }

                if (IsPlayers(inv))
                {
                    players = true;
                    break;
                }

                n++;
            }

            return facts with
            {
                TargetInventories = n,
                TargetPlayers = players,
            };
        }

        private static IEnumerable<GameObject> Targets(GameEvent ge)
        {
            if (ge.targetGameObjects != null)
            {
                foreach (var go in ge.targetGameObjects)
                {
                    if (go != null)
                    {
                        yield return go;
                    }
                }
            }

            if (ge.targetUniqueObjects != null
                && Singleton<UniqueObjects>.Instance != null)
            {
                foreach (var name in ge.targetUniqueObjects)
                {
                    var go = Singleton<UniqueObjects>.Instance.getObject(name);
                    if (go != null)
                    {
                        yield return go;
                    }
                }
            }
        }

        private static bool IsPlayers(Inventory inv)
        {
            var p = Player.Instance;
            bool under = inv.GetComponentInParent<Player>() != null;
            bool bag = p != null && inv == p.Inventory;
            bool hotbar = p != null && inv == p.Hotbar;
            return WorldMoveRules.IsPlayers(under, bag, hotbar);
        }

        /// <summary>
        /// Host: run a world move's coroutine, then send the source's and the
        /// targets' contents.
        /// </summary>
        internal static IEnumerator SendAfter(IEnumerator inner, GameEvent ge)
        {
            while (inner.MoveNext())
            {
                yield return inner.Current;
            }

            CoopSession coop = Coop.Current;
            bool host = S != null && S.IsHost;
            if (!WorldMoveRules.SendsAfter(Together, host, coop != null))
            {
                yield break;
            }

            Containers containers = coop.Containers;

            Inventory src = null;
            if (ge.sourceTransform != null)
            {
                src = ge.sourceTransform.GetComponent<Inventory>();
            }

            if (src != null)
            {
                containers.SendContents(src);
            }

            foreach (var go in Targets(ge))
            {
                var inv = go.GetComponent<Inventory>();
                if (inv != null)
                {
                    containers.SendContents(inv);
                }
            }

            if (Debug)
            {
                Transform sourceTransform = ge.sourceTransform;
                string name = null;
                if (sourceTransform != null)
                {
                    name = sourceTransform.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] moveInventory from {name} done here; sent the "
                        + "containers");
            }
        }
    }
}

