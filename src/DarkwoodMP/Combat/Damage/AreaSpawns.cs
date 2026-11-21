using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// What the host's enemies spawned (projectiles, and what their
    /// explosions spawned: fire) and what the local player's weapon spawned
    /// (a flamethrower's flames), for EnemyAreaHits, BreakSync and ThrowSync.
    /// It lives on ModHost, as long as the game: the test dummy is hurt with
    /// no session too. It is emptied when a world loads and when a session
    /// ends.
    /// </summary>
    internal sealed class AreaSpawns
    {
        private readonly HashSet<GameObject> _enemy =
            new HashSet<GameObject>();
        private readonly HashSet<GameObject> _player =
            new HashSet<GameObject>();

        /// <summary>
        /// How deep we are in an enemy's spawning call (its projectile, or
        /// an explosion of something it spawned).
        /// </summary>
        public int EnemyDepth;

        /// <summary>
        /// How deep we are in our player's Player.spawnBullet.
        /// </summary>
        public int PlayerDepth;

        /// <summary>
        /// Whether an object the game makes now goes in a set.
        /// </summary>
        public bool Spawning => EnemyDepth > 0 || PlayerDepth > 0;

        public void Clear()
        {
            _enemy.Clear();
            _player.Clear();
            EnemyDepth = 0;
            PlayerDepth = 0;
        }

        public void AddEnemy(GameObject go) => Add(_enemy, go);

        public void AddPlayer(GameObject go) => Add(_player, go);

        /// <summary>
        /// Whether one of the host's enemies spawned it or a parent of it.
        /// </summary>
        public bool ByEnemy(Transform t) => In(_enemy, t);

        /// <summary>
        /// Whether our player's weapon spawned it or a parent of it.
        /// </summary>
        public bool ByPlayer(Transform t) => In(_player, t);

        private static void Add(HashSet<GameObject> set, GameObject go)
        {
            if (AreaHitRules.Prunes(set.Count))
            {
                set.RemoveWhere(g => g == null);
            }

            set.Add(go);
        }

        private static bool In(HashSet<GameObject> set, Transform t)
        {
            if (set.Count == 0)
            {
                return false;
            }

            for (; t != null; t = t.parent)
            {
                if (set.Contains(t.gameObject))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
