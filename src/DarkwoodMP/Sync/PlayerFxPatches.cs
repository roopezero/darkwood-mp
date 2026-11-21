using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    // Each patch hands its call to the session's PlayerFx; with no session
    // they do nothing.

    /// <summary>
    /// A sound the game played: if it's on our player (or by our player's own
    /// code), the others hear it too.
    /// </summary>
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    static class AudioControllerPlayAsSound
    {
        static void Postfix(
            string audioID, float volume, Transform parentObj,
            AudioObject __result)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.Played(audioID, volume, parentObj, __result);
            }
        }
    }

    /// <summary>
    /// Player's methods that play its sounds at the listener rather than on
    /// the player (see PlayerFx.IsOurs).
    /// </summary>
    [HarmonyPatch]
    static class PlayerSoundCode
    {
        private static readonly string[] Names =
        {
            "onReleaseAim", "reload", "OnAniFinish", "initiateAttack",
            "InitiateEquipmentUsage", "attack",
            "getHitByShadow", "aim", "onActivateItem", "switchToItem",
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in Names)
            {
                var m = AccessTools.DeclaredMethod(typeof(Player), n);
                if (m != null)
                {
                    yield return m;
                }
                else
                {
                    Plugin.Log.LogWarning(
                        $"[fx] Player.{n} isn't in this build: its sounds "
                            + "won't be heard by the others");
                }
            }

            // getHit has two: the one that does it.
            yield return AccessTools.DeclaredMethod(typeof(Player),
                nameof(Player.getHit),
                new[]
                {
                    typeof(float),
                    typeof(Transform),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                });

            // The coroutine that fires a gun again (bursts, and the click of
            // an empty one).
            foreach (var t in typeof(Player).GetNestedTypes(
                BindingFlags.NonPublic))
            {
                if (t.Name.Contains("waitToFireWeapon"))
                {
                    var m = AccessTools.Method(t, "MoveNext");
                    if (m != null)
                    {
                        yield return m;
                    }
                }
            }
        }

        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.EnterPlayerCode();
            }
        }

        static void Finalizer()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.LeavePlayerCode();
            }
        }
    }

    /// <summary>
    /// Our player fired a gun: the others see the muzzle flash on our puppet.
    /// </summary>
    [HarmonyPatch(typeof(Player), "fireWeapon")]
    static class PlayerFireWeapon
    {
        static void Postfix(Player __instance)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.Fired(__instance);
            }
        }
    }

    /// <summary>
    /// The blood splats of our player's getHit: shown on our puppet too.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.getHit),
        new[]
        {
            typeof(float),
            typeof(Transform),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    static class PlayerGetHitBlood
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.EnterGetHit();
            }
        }

        static void Finalizer()
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.LeaveGetHit();
            }
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(string),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class CoreAddPrefabBlood
    {
        static void Postfix(string prefab, GameObject __result)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.PlayerFx.Added(prefab, __result);
            }
        }
    }
}
