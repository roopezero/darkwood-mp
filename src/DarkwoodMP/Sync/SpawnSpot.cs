using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Where a joining player's character is put: the spot it should have (beside the host, or where it was saved)
    /// if that's free, or else the nearest free spot around it. A spot is free when no other player stands within
    /// two body widths of it, nothing solid (a wall, furniture, a character) overlaps a body there, the game's
    /// pathfinding grid counts it as walkable, and there's no wall between it and the spot it should have (so a
    /// joiner isn't put in the next room). Without this a joiner appeared on top of the host, and the two bodies
    /// shoved each other apart, often into a corner.
    ///
    /// Colliders that already overlap the middle of the wanted spot (big volumes: a location's, a room's) don't
    /// count, as a player can stand inside them.
    /// </summary>
    public static class SpawnSpot
    {
        private const int Angles = 12;
        private static readonly float[] Rings = { 2.5f, 4f, 6f, 8f, 11f }; // in body radii from the wanted spot
        private static readonly Collider[] Hits = new Collider[64];

        /// <summary>
        /// A free spot for player p near want. taken: other spots to keep clear (where the host is, when the
        /// joiner's game may not have heard from the host yet). Returns want when it's free, or when none is.
        /// </summary>
        public static Vector3 Find(Player p, Vector3 want, IEnumerable<Vector3> taken = null)
        {
            float radius = BodyRadius(p);
            var others = new List<Vector3>();
            if (taken != null) others.AddRange(taken);
            var sync = PlayerSync.Instance;
            if (sync != null)
                foreach (var r in sync.Remotes.Values)
                {
                    var last = r.Last;
                    if (last == null || last.Spectating || !World.Locations.SamePlace(last.Away, last.Place)) continue;
                    others.Add(last.Position);
                }

            var background = new HashSet<Collider>();
            int n = Physics.OverlapSphereNonAlloc(want, radius * 0.5f, Hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) background.Add(Hits[i]);
            bool gridHere = Walkable(want);

            if (Free(p, want, want, radius, others, background, gridHere)) return want;
            float start = Random.Range(0f, 360f); // two joiners at once don't both pick the first free angle
            foreach (var ring in Rings)
                for (int a = 0; a < Angles; a++)
                {
                    float ang = (start + a * 360f / Angles) * Mathf.Deg2Rad;
                    var at = want + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (ring * radius);
                    if (Free(p, want, at, radius, others, background, gridHere)) return at;
                }
            Plugin.Log.LogInfo($"[spawn] no free spot within {Rings[Rings.Length - 1] * radius:0} of {want}: staying there");
            return want;
        }

        /// <summary>The radius of the player's body (its capsule), at least 10.</summary>
        public static float BodyRadius(Player p)
        {
            var cap = p != null ? p.GetComponent<CapsuleCollider>() : null;
            float r = cap != null ? cap.radius * Mathf.Max(Mathf.Abs(p.transform.lossyScale.x), Mathf.Abs(p.transform.lossyScale.z)) : 15f;
            return Mathf.Max(10f, r);
        }

        /// <summary>Two players' bodies that close (flat distance) overlap or touch.</summary>
        public static float Apart(Player p) => BodyRadius(p) * 2f + 10f;

        private static bool Free(Player p, Vector3 want, Vector3 at, float radius, List<Vector3> others, HashSet<Collider> background, bool gridHere)
        {
            float apart = radius * 2f + 10f;
            foreach (var o in others)
                if (new Vector2(o.x - at.x, o.z - at.z).magnitude < apart) return false;

            int n = Physics.OverlapSphereNonAlloc(at, radius * 1.1f, Hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Solid(p, Hits[i], background)) return false;

            if (gridHere && !Walkable(at)) return false;

            var d = at - want;
            float len = d.magnitude;
            if (len > 1f)
            {
                var hits = Physics.RaycastAll(want, d / len, len, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                    if (h.collider.attachedRigidbody == null && Solid(p, h.collider, background)) return false; // a wall between
            }
            return true;
        }

        private static bool Solid(Player p, Collider c, HashSet<Collider> background)
        {
            if (c == null || c.isTrigger || background.Contains(c)) return false;
            if (c.gameObject.layer == 1) return false; // the ground (Core.canSpawnHere skips it too)
            if (p != null && c.transform.IsChildOf(p.transform)) return false;
            if (c.GetComponentInParent<Combat.NetPlayerProxy>() != null) return false; // counted by position instead
            return true;
        }

        /// <summary>The game's pathfinding grid counts it walkable (Core.isPointWalkable); true if there's no grid.</summary>
        private static bool Walkable(Vector3 at)
        {
            try { return Core.isPointWalkable(at); }
            catch { return true; }
        }
    }
}
