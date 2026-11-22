using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// "--dwmp-test-blast" (host): once a player (not the dummy) has joined,
    /// three of the host's enemies' kinds of area damage go off at them: an
    /// enemy's bomb exploding beside them (DarkwoodMP_TestBlast, with a real
    /// bomb's sound and flash, so EnemyExplosion is sent), the nearest enemy's
    /// damage aura (named DarkwoodMP_TestAura while it hurts), and an enemy's
    /// flame touching them (DarkwoodMP_TestFlame). Each should reach them as
    /// PlayerDamage.
    /// </summary>
    static class AreaHitsTest
    {
        private static readonly System.Reflection.MethodInfo AuraTick =
            AccessTools.Method(typeof(Character), "waitToDamageAroundMe");
        private static readonly System.Reflection.MethodInfo FlameTouch =
            AccessTools.Method(typeof(Flame), "onCollideWith");

        /// <summary>
        /// A joined player's proxy (not the dummy's), or null.
        /// </summary>
        internal static NetPlayerProxy Joined()
        {
            NetPlayerProxy p = null;
            foreach (var x in NetPlayerProxy.All.Values)
            {
                if (x != null && x.Owner != Sync.Players.PlayerSync.DummyId)
                {
                    p = x;
                }
            }

            return p;
        }

        /// <summary>
        /// The living enemy of the host's nearest to a position, or null.
        /// </summary>
        internal static Character NearestEnemy(Vector3 at)
        {
            Character c = null;
            foreach (var x in Plugin.Instance.Host.Characters.Active())
            {
                if (x != null && x.alive && NetEnemies.Eligible(x)
                    && (c == null
                        || (x.transform.position - at).sqrMagnitude
                            < (c.transform.position - at).sqrMagnitude))
                {
                    c = x;
                }
            }

            return c;
        }

        internal static System.Collections.IEnumerator Run()
        {
            NetPlayerProxy p;
            while ((p = Joined()) == null)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            if (p == null)
            {
                yield break;
            }

            var at = p.transform.position;

            // A bomb: a real one's sound and flash, no fire.
            var go = new GameObject("DarkwoodMP_TestBlast");
            go.transform.position = at + new Vector3(10f, 0f, 0f);
            var ex = go.AddComponent<Explodes>();
            var db = Singleton<ItemsDatabase>.Instance;
            IEnumerable<string> keys;
            if (db != null)
            {
                keys = db.itemsDict.Keys;
            }
            else
            {
                keys = (IEnumerable<string>)new string[0];
            }

            foreach (var key in keys)
            {
                var item = db.getItem(key, instantiate: false);
                Explodes real = null;
                if (item != null && item.item is GameObject g)
                {
                    real = g.GetComponent<Explodes>();
                }

                if (real == null || real.explosionPrefab == null)
                {
                    continue;
                }

                ex.explosionPrefab = real.explosionPrefab;
                ex.explodeSound = real.explodeSound;
                break;
            }

            ex.radius = 300f;
            ex.damage = 30f;
            ex.affectsPlayer = true;
            ex.effect = new InvItemEffect();
            EnemyAreaHits.MarkEnemy(go);
            string flash = "no flash";
            if (ex.explosionPrefab != null)
            {
                flash = ex.explosionPrefab.name;
            }

            Plugin.Log.LogInfo(
                $"--dwmp-test-blast: an enemy bomb ({flash}) beside "
                    + $"{p.OwnerName}");
            ex.onActivate();
            Object.Destroy(go);
            yield return new WaitForSeconds(1f);

            // The nearest enemy's damage aura, made wide enough to reach them.
            var c = NearestEnemy(at);
            if (c != null)
            {
                string name = c.name;
                int dmg = c.aroundMeDamage;
                float range = c.aroundMeDamageRange;
                c.name = "DarkwoodMP_TestAura";
                c.aroundMeDamage = 6;
                c.aroundMeDamageRange = 2f * Core.trueDistance(
                    c.transform.position, p.transform.position) + 200f;
                Plugin.Log.LogInfo($"--dwmp-test-blast: {name}'s damage aura");
                try
                {
                    AuraTick.Invoke(c, null);
                }
                finally
                {
                    c.name = name;
                    c.aroundMeDamage = dmg;
                    c.aroundMeDamageRange = range;
                }
            }
            else
            {
                Plugin.Log.LogInfo(
                    "--dwmp-test-blast: no enemy for the damage aura");
            }

            yield return new WaitForSeconds(1f);

            // An enemy's flame.
            if (p == null)
            {
                yield break;
            }

            var fgo = new GameObject("DarkwoodMP_TestFlame");
            fgo.transform.position = p.transform.position;
            var f = fgo.AddComponent<Flame>();
            f.contactDamage = 4;
            f.effect = new InvItemEffect
            {
                type = CharacterEffectType.burn,
                duration = 3f,
                modifier = 1f,
                interval = 1f
            };

            EnemyAreaHits.MarkEnemy(fgo);
            Plugin.Log.LogInfo(
                $"--dwmp-test-blast: an enemy's flame on {p.OwnerName}");
            FlameTouch.Invoke(
                f,
                new object[] { p.transform, p.GetComponent<Collider>(), true });
            Object.Destroy(fgo, 2f);
            Plugin.Log.LogInfo("--dwmp-test-blast: done");
        }
    }
}
