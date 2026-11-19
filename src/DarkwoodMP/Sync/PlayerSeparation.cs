using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Soft collision between players: a player standing in another player is eased out of them, gently, instead of
    /// the two passing through each other or bumping hard.
    ///
    /// Players' bodies don't collide (the puppets have no solid colliders, and on the host the stand-ins for the AI
    /// ignore its player), so nothing in the physics keeps them apart. Each game moves only its own player: away
    /// from every other player's puppet nearer than two body widths, faster the deeper they overlap (MaxSpeed at the
    /// centre, nothing at the edge). Both games do the same, so two players standing in each other each take half of
    /// the way out. It's a nudge to the player's rigidbody, so walls still stop it, and walking against another
    /// player only slows you down: you can push past. The player's own game decides where it is, as for any walk,
    /// and the host's MoveCheck sees an ordinary slow move.
    /// </summary>
    public static class PlayerSeparation
    {
        private const float MaxSpeed = 120f;    // world units a second, with the two fully overlapping (walking is about 250)
        private const float DefaultRadius = 18f;
        private static float _radius;          // our player's body, along the ground
        private static Player _measured;

        /// <summary>Total distance our player was eased out of others (for the tests' log).</summary>
        public static float Eased;

        /// <summary>How close two players' centres can be before they're eased apart (two body widths).</summary>
        public static float Distance => 2f * (_radius > 0f ? _radius : DefaultRadius);

        public static void Update(PlayerSync sync, ulong localId)
        {
            ModConfig cfg = Plugin.Cfg;
            if (cfg != null && cfg.SoftPlayerCollision != null && !Plugin.Cfg.SoftPlayerCollision.Value)
            {
                return;
            }
            if (!World.WorldShare.InWorld || sync.Remotes.Count == 0)
            {
                return;
            }
            var p = Player.Instance;
            if (p == null || !p.alive || p.dying || Core.forbidInputs)
            {
                return;
            }
            var rb = p.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
            {
                return;
            }
            if (_measured != p)
            {
                Measure(p);
            }

            float min = Distance;
            var me = rb.position;
            Vector3 push = Vector3.zero;
            foreach (var r in sync.Remotes.Values)
            {
                if (r.Root == null || !r.Root.activeSelf || !r.Alive || r.Spectating)
                {
                    continue;
                }
                var other = r.Root.transform.position;
                var d = new Vector3(me.x - other.x, 0f, me.z - other.z);
                float dist = d.magnitude;
                if (dist >= min)
                {
                    continue;
                }
                // Right on top of each other: each game goes its own way, by who has the lower id.
                Vector3 away;
                if (dist > 0.01f)
                {
                    away = d / dist;
                }
                else if (localId < r.Owner)
                {
                    away = Vector3.right;
                }
                else
                {
                    away = Vector3.left;
                }
                push += away * ((min - dist) / min);
            }
            if (push == Vector3.zero)
            {
                return;
            }
            float frac = Mathf.Min(1f, push.magnitude);
            var step = push.normalized * (MaxSpeed * frac * Mathf.Min(Time.deltaTime, 0.05f));
            rb.MovePosition(rb.position + step);
            Eased += step.magnitude;
        }

        private static void Measure(Player p)
        {
            _measured = p;
            _radius = 0f;
            foreach (var c in p.GetComponents<Collider>())
            {
                if (c != null && !c.isTrigger && c.enabled)
                {
                    _radius = Mathf.Max(_radius, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z));
                }
            }
            if (_radius <= 1f || _radius > 60f)
            {
                _radius = DefaultRadius;
            }
            Plugin.Log.LogInfo($"Soft player collision: players are eased apart within {Distance:0} units");
        }
    }
}
