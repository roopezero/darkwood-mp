using System.Collections.Generic;
using DarkwoodMP.Sync.Players;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Where a joining player's character is put: the spot it should have
    /// (beside the host, or where it was saved) if that's free, or else the
    /// nearest free spot around it. A spot is free when no other player stands
    /// within two body widths of it, nothing solid (a wall, furniture, a
    /// character) overlaps a body there, the game's pathfinding grid counts it
    /// as walkable, and there's no wall between it and the spot it should have
    /// (so a joiner isn't put in the next room). Without this a joiner appeared
    /// on top of the host, and the two bodies shoved each other apart, often
    /// into a corner.
    ///
    /// Colliders that already overlap the middle of the wanted spot (big
    /// volumes: a location's, a room's) don't count, as a player can stand
    /// inside them.
    /// </summary>
    public static class SpawnSpot
    {
        private static readonly Collider[] Hits =
            new Collider[SpawnRules.MaxHits];

        /// <summary>
        /// A free spot for player p near want. taken: other spots to keep clear
        /// (where the host is, when the joiner's game may not have heard from
        /// the host yet). Returns want when it's free, or when none is.
        /// </summary>
        public static Vector3 Find(
            Player p, Vector3 want, IEnumerable<Vector3> taken = null)
        {
            float radius = BodyRadius(p);
            var others = new List<Vec2>();
            if (taken != null)
            {
                foreach (var t in taken)
                {
                    others.Add(Flat(t));
                }
            }

            var sync = Plugin.Instance.Host.Players;
            if (sync != null)
            {
                foreach (var r in sync.Remotes.Values)
                {
                    var last = r.Last;
                    if (last == null)
                    {
                        continue;
                    }

                    bool same =
                        World.Locations.SamePlace(last.Away, last.Place);
                    if (SpawnRules.KeepsClear(true, last.Spectating, same))
                    {
                        others.Add(Flat(last.Position));
                    }
                }
            }

            var background = new HashSet<Collider>();
            int n = Physics.OverlapSphereNonAlloc(
                want, radius * SpawnRules.BackgroundShare, Hits, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                background.Add(Hits[i]);
            }

            bool gridHere = Walkable(want);

            if (Free(p, want, want, radius, others, background, gridHere))
            {
                return want;
            }

            // two joiners at once don't both pick the first free angle
            float start = Random.Range(0f, 360f);
            foreach (var spot in SpawnRules.Around(VecOf(want), radius, start))
            {
                var at = new Vector3(spot.X, spot.Y, spot.Z);
                if (Free(p, want, at, radius, others, background, gridHere))
                {
                    return at;
                }
            }

            Plugin.Log.LogInfo(
                "[spawn] no free spot within "
                    + $"{SpawnRules.Searched(radius):0} of {want}: "
                    + "staying there");
            return want;
        }

        /// <summary>
        /// The radius of the player's body (its capsule), at least 10.
        /// </summary>
        public static float BodyRadius(Player p)
        {
            CapsuleCollider cap = null;
            if (p != null)
            {
                cap = p.GetComponent<CapsuleCollider>();
            }

            if (cap == null)
            {
                return SpawnRules.BodyRadius(false, 0f, default);
            }

            Vector3 scale = p.transform.lossyScale;
            return SpawnRules.BodyRadius(
                true, cap.radius, new Vec2(scale.x, scale.z));
        }

        /// <summary>
        /// Two players' bodies that close (flat distance) overlap or touch.
        /// </summary>
        public static float Apart(Player p) =>
            SpawnRules.Apart(BodyRadius(p));

        private static bool Free(
            Player p, Vector3 want, Vector3 at, float radius,
            List<Vec2> others, HashSet<Collider> background, bool gridHere)
        {
            float apart = SpawnRules.Apart(radius);
            if (!SpawnRules.ClearOfOthers(Flat(at), others, apart))
            {
                return false;
            }

            int n = Physics.OverlapSphereNonAlloc(
                at, radius * SpawnRules.SphereShare, Hits, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (Solid(p, Hits[i], background))
                {
                    return false;
                }
            }

            if (!SpawnRules.WalkableEnough(gridHere, Walkable(at)))
            {
                return false;
            }

            var d = at - want;
            float len = d.magnitude;
            if (SpawnRules.ChecksRay(len))
            {
                var hits = Physics.RaycastAll(
                    want, d / len, len, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    bool body = h.collider.attachedRigidbody != null;
                    if (SpawnRules.BlocksRay(
                        body, Solid(p, h.collider, background)))
                    {
                        return false; // a wall between
                    }
                }
            }

            return true;
        }

        private static bool Solid(
            Player p, Collider c, HashSet<Collider> background)
        {
            return SpawnRules.IsSolid(SolidOf(p, c, background));
        }

        // The ground (layer 1) is skipped as Core.canSpawnHere does; a proxy
        // is counted by position instead.
        private static SolidFacts SolidOf(
            Player p, Collider c, HashSet<Collider> background)
        {
            if (c == null)
            {
                return default;
            }

            return new SolidFacts
            {
                Exists = true,
                Trigger = c.isTrigger,
                Background = background.Contains(c),
                Layer = c.gameObject.layer,
                OfPlayer = p != null && c.transform.IsChildOf(p.transform),
                OfProxy =
                    c.GetComponentInParent<Combat.NetPlayerProxy>() != null,
            };
        }

        private static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// The game's pathfinding grid counts it walkable
        /// (Core.isPointWalkable); true if there's no grid.
        /// </summary>
        private static bool Walkable(Vector3 at)
        {
            try
            {
                return Core.isPointWalkable(at);
            }
            catch
            {
                return true;
            }
        }
    }
}
