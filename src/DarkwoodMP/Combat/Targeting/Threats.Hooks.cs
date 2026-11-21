using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Threats' answers to the game's calls that ThreatPatches and
    /// FleePatches hand on. They run with no session too: the test dummy
    /// brings threat into a game played alone. See Threats.cs.
    /// </summary>
    internal sealed partial class Threats
    {
        // the game's radius and layers for checkIfBeingChased
        private const float ChaseRadius = 500f;
        private const int ChaseLayers = 2099200;

        private readonly Collider[] _near = new Collider[256];

        /// <summary>
        /// Character.getHit has run: damage a player dealt to an enemy is
        /// threat for that player; the host shows the hit's blood to the
        /// clients. before is the enemy's health before the hit.
        /// </summary>
        public void Hit(Character victim, in GameHit hit, float before)
        {
            float dealt = before - victim.health;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Enemies.HitTaken(victim, new GameHit
                {
                    Damage = dealt,
                    Attacker = hit.Attacker,
                    NormalHit = hit.NormalHit,
                });
            }

            if (!Enabled || hit.Attacker == null)
            {
                return;
            }

            if (dealt > 0f)
            {
                OnDamaged(
                    victim, hit.Attacker.GetComponent<CharBase>(), dealt);
            }
        }

        /// <summary>
        /// Character.heardSound: noise is threat for the player who made it.
        /// </summary>
        public void Heard(Character listener, Vector3 position, float volume)
        {
            if (Enabled && !listener.deaf && listener.alive)
            {
                OnHeard(listener, position, volume);
            }
        }

        /// <summary>
        /// Character.attackPlayer runs as in the game: "attack the player"
        /// (scripted attacks, spawns that come for the player) means the
        /// player with the most threat, so the game's own goes only for the
        /// host's player.
        /// </summary>
        public bool AttacksHostPlayer(Character c)
        {
            if (!Enabled || c.dummy)
            {
                return true;
            }

            // An NPC a joiner's event sends after "the player" (a dialogue's
            // outcome, run here for them) goes for them.
            NetPlayerProxy requester = null;
            CoopSession coop = Coop.Current;
            if (c.npc != null && coop != null)
            {
                requester = coop.Authority.Requester;
            }

            if (requester != null)
            {
                HostileNpcs.SendAfter(c, requester);
                return false;
            }

            var best = BestPlayerFor(c);
            if (best == null || best is Player)
            {
                return true; // the host's player: as the game does it
            }

            JoinerAi.GoFor(c, best.transform);
            if (Debug)
            {
                Plugin.Log.LogInfo(
                    $"[threat] {c.name} sent after the player: goes "
                        + $"for {((NetPlayerProxy)best).OwnerName}");
            }

            return false;
        }

        /// <summary>
        /// Character.checkIfBeingChased runs as in the game: with others in
        /// the world, an escaping enemy runs from the nearest living player,
        /// and one on its way out from where FleeFrom says.
        /// </summary>
        public bool ChecksChase(Character c)
        {
            if (!Enabled)
            {
                return true;
            }

            if (c.summonsAfterEscaping)
            {
                return false;
            }

            if (c.wantToDespawn)
            {
                c.runAway(FleeFrom(c));
                return false;
            }

            if (c.behaviour != Character.Behaviour.escaping)
            {
                return false;
            }

            CharBase nearest = NearestChaser(c.transform.position);
            if (nearest != null)
            {
                c.runAway(nearest.transform.position);
            }

            return false;
        }

        private CharBase NearestChaser(Vector3 me)
        {
            int n = Physics.OverlapSphereNonAlloc(
                me, ChaseRadius, _near, ChaseLayers);
            CharBase nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                CharBase cb = null;
                if (_near[i] != null)
                {
                    cb = _near[i].GetComponent<CharBase>();
                }

                if (!Proxies.IsPlayer(cb)
                    || (cb is NetPlayerProxy && !cb.alive))
                {
                    continue;
                }

                float d = Core.trueDistance(cb.transform.position, me);
                if (d < best)
                {
                    best = d;
                    nearest = cb;
                }
            }

            return nearest;
        }
    }
}
