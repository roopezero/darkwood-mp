using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    // EnemyAreaHits' patches run with no session too: the test dummy is hurt
    // when playing alone. Playing alone with no dummy, Hosting is false and
    // they change nothing but the spawn sets.

    // ---- what enemies and the player's weapon spawn -------------------------

    [HarmonyPatch]
    static class CharacterSpawnsProjectile
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Character), "rangedAttack");
            yield return AccessTools.Method(
                typeof(Character), nameof(Character.spawnProjectile));
        }

        static void Prefix() => EnemyAreaHits.Spawns.EnemyDepth++;
        static void Finalizer() => EnemyAreaHits.Spawns.EnemyDepth--;
    }

    /// <summary>
    /// What an enemy's projectile spawns as it explodes (fire) is the enemy's
    /// too.
    /// </summary>
    [HarmonyPatch(
        typeof(Explodes), nameof(Explodes.onActivate), new System.Type[0])]
    static class ExplodesOnActivateByEnemy
    {
        static void Prefix(Explodes __instance, out bool __state)
        {
            __state = EnemyAreaHits.ByEnemy(
                __instance.transform, __instance.objectThatSpawnedMe);
            if (__state)
            {
                EnemyAreaHits.Spawns.EnemyDepth++;
            }
        }

        static void Finalizer(bool __state)
        {
            if (__state)
            {
                EnemyAreaHits.Spawns.EnemyDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "spawnBullet")]
    static class PlayerSpawnBullet
    {
        static void Prefix() => EnemyAreaHits.Spawns.PlayerDepth++;
        static void Finalizer() => EnemyAreaHits.Spawns.PlayerDepth--;
    }

    [HarmonyPatch]
    static class CoreAddPrefabSpawned
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Core),
                nameof(Core.AddPrefab), new[]
            {
                typeof(Object),
                typeof(Vector3),
                typeof(Quaternion),
                typeof(GameObject),
                typeof(bool)
            });

            yield return AccessTools.Method(typeof(Core),
                nameof(Core.AddPrefab), new[]
            {
                typeof(string),
                typeof(Vector3),
                typeof(Quaternion),
                typeof(GameObject),
                typeof(bool)
            });
        }

        static void Postfix(GameObject __result)
        {
            if (EnemyAreaHits.Spawns.Spawning)
            {
                EnemyAreaHits.Spawned(__result);
            }
        }
    }

    [HarmonyPatch]
    static class CoreAddPooledPrefabSpawned
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(Core)).FindAll(
                m => m.Name == nameof(Core.AddPooledPrefab)
                    && m.ReturnType == typeof(Transform));
        static void Postfix(Transform __result)
        {
            if (__result != null && EnemyAreaHits.Spawns.Spawning)
            {
                EnemyAreaHits.Spawned(__result.gameObject);
            }
        }
    }

    // ---- the hits -----------------------------------------------------------

    [HarmonyPatch(typeof(Explodes), "explode")]
    static class ExplodesExplodeStandIns
    {
        static void Prefix(Explodes __instance) =>
            EnemyAreaHits.Explosion(__instance);
    }

    [HarmonyPatch(typeof(Character), "waitToDamageAroundMe")]
    static class CharacterWaitToDamageAroundMe
    {
        static void Prefix(Character __instance) =>
            EnemyAreaHits.Aura(__instance);
    }

    /// <summary>
    /// Host: a diving flier hurts the player it dove at (the game hurts
    /// Player.Instance). The transpiler swaps "Player.Instance.getHit(5f)" for
    /// EnemyAreaHits.FlierHit(this, 5f). Client: a frozen copy doesn't dive.
    /// </summary>
    [HarmonyPatch(typeof(Flier), "Update")]
    static class FlierUpdate
    {
        static bool Prefix(Flier __instance) =>
            !AreaHitRules.Frozen(
                NetEnemies.ClientMode,
                NetEnemies.ClientMode && NetEnemies.Frozen.Contains(
                    __instance.GetComponent<Character>()));

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var getInstance = AccessTools.PropertyGetter(
                typeof(Player), nameof(Player.Instance));
            var getHit = AccessTools.Method(
                typeof(Player), nameof(Player.getHit), new[] { typeof(float) });
            var getHitBase = AccessTools.Method(
                typeof(CharBase), nameof(CharBase.getHit),
                new[] { typeof(float) });
            var flierHit = AccessTools.Method(
                typeof(EnemyAreaHits), nameof(EnemyAreaHits.FlierHit));
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count - 2; i++)
            {
                if (!list[i].Calls(getInstance))
                {
                    continue;
                }

                if (!(list[i + 2].Calls(getHit) || list[i + 2]
                        .Calls(getHitBase)))
                {
                    continue;
                }

                list[i] = new CodeInstruction(
                    System.Reflection.Emit.OpCodes.Ldarg_0)
                {
                    labels = list[i].labels
                };

                list[i + 2] = new CodeInstruction(
                    System.Reflection.Emit.OpCodes.Call, flierHit)
                {
                    labels = list[i + 2].labels
                };

                swapped++;
            }

            if (swapped == 0)
            {
                Plugin.Log.LogWarning(
                    "FlierUpdate: no Player.Instance.getHit in "
                        + "Flier.Update (game changed?)");
            }

            return list;
        }
    }

    [HarmonyPatch(typeof(Shooter), "shoot")]
    static class ShooterShoot
    {
        static bool Prefix(Shooter __instance)
        {
            // A client's frozen copy of the host's enemy doesn't shoot.
            if (AreaHitRules.Frozen(
                    NetEnemies.ClientMode,
                    NetEnemies.ClientMode && NetEnemies.Frozen.Contains(
                        __instance.GetComponentInParent<Character>())))
            {
                return false;
            }

            EnemyAreaHits.Shot(__instance);
            return true;
        }
    }
}
