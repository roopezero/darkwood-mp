using System.Collections.Generic;
using DarkwoodMP.Sync.Players;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Soft collision between players: a player standing in another player is
    /// eased out of them, gently, instead of the two passing through each other
    /// or bumping hard.
    ///
    /// Players' bodies don't collide (the puppets have no solid colliders, and
    /// on the host the stand-ins for the AI ignore its player), so nothing in
    /// the physics keeps them apart. Each game moves only its own player: away
    /// from every other player's puppet nearer than two body widths, faster the
    /// deeper they overlap (MaxSpeed at the centre, nothing at the edge). Both
    /// games do the same, so two players standing in each other each take half
    /// of the way out. It's a nudge to the player's rigidbody, so walls still
    /// stop it, and walking against another player only slows you down: you can
    /// push past. The player's own game decides where it is, as for any walk,
    /// and the host's MoveCheck sees an ordinary slow move.
    /// </summary>
    public static class PlayerSeparation
    {
        // our player's body, along the ground
        private static float _radius;
        private static Player _measured;

        /// <summary>
        /// Total distance our player was eased out of others (for the tests'
        /// log).
        /// </summary>
        public static float Eased;

        /// <summary>
        /// How close two players' centres can be before they're eased apart
        /// (two body widths).
        /// </summary>
        public static float Distance => SeparationRules.Distance(_radius);

        public static void Update(PlayerSync sync, ulong localId)
        {
            var p = Player.Instance;
            Rigidbody rb = null;
            if (p != null)
            {
                rb = p.GetComponent<Rigidbody>();
            }

            if (!SeparationRules.Eases(FrameOf(sync, p, rb)))
            {
                return;
            }

            if (_measured != p)
            {
                Measure(p);
            }

            Vec2 step = SeparationRules.Step(PushOf(sync, rb, localId));
            var move = new Vector3(step.X, 0f, step.Z);
            if (move == Vector3.zero)
            {
                return;
            }

            rb.MovePosition(rb.position + move);
            Eased += move.magnitude;
        }

        private static SeparationFacts FrameOf(
            PlayerSync sync, Player p, Rigidbody rb)
        {
            ModConfig cfg = Plugin.Cfg;
            return new SeparationFacts
            {
                SwitchedOff = cfg != null && cfg.SoftPlayerCollision != null
                    && !cfg.SoftPlayerCollision.Value,
                InWorld = World.WorldShare.InWorld,
                Puppets = sync.Remotes.Count,
                HasPlayer = p != null,
                Alive = p != null && p.alive,
                Dying = p != null && p.dying,
                ForbidInputs = Core.forbidInputs,
                HasBody = rb != null,
                Kinematic = rb != null && rb.isKinematic,
            };
        }

        private static PushFacts PushOf(
            PlayerSync sync, Rigidbody rb, ulong localId)
        {
            var bodies = new List<PuppetBody>(sync.Remotes.Count);
            foreach (var r in sync.Remotes.Values)
            {
                bool shown = r.Root != null && r.Root.activeSelf;
                Vec2 at = default;
                if (shown)
                {
                    var other = r.Root.transform.position;
                    at = new Vec2(other.x, other.z);
                }

                bodies.Add(new PuppetBody
                {
                    Owner = r.Owner,
                    Shown = shown,
                    Alive = r.Alive,
                    Spectating = r.Spectating,
                    At = at,
                });
            }

            var me = rb.position;
            return new PushFacts
            {
                Ours = new Vec2(me.x, me.z),
                LocalId = localId,
                Radius = _radius,
                Delta = new Seconds(Time.deltaTime),
                Puppets = bodies,
            };
        }

        private static ColliderFacts ColliderOf(Collider c)
        {
            if (c == null)
            {
                return default;
            }

            var e = c.bounds.extents;
            return new ColliderFacts
            {
                Exists = true,
                Trigger = c.isTrigger,
                Enabled = c.enabled,
                Extents = new Vec2(e.x, e.z),
            };
        }

        private static void Measure(Player p)
        {
            _measured = p;
            var colliders = new List<ColliderFacts>();
            foreach (var c in p.GetComponents<Collider>())
            {
                colliders.Add(ColliderOf(c));
            }

            _radius = SeparationRules.Radius(colliders);

            Plugin.Log.LogInfo(
                "Soft player collision: players are eased apart within "
                    + $"{Distance:0} units");
        }
    }
}
