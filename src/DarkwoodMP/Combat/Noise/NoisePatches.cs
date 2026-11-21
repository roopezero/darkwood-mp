using System.Collections.Generic;
using DarkwoodMP.Combat.Noise;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Character.alertInArea is how the game makes noise enemies hear. Raised
    /// from the Player's own code (steps, running, shots), it's that player's
    /// noise: on the host it's threat for them; on a client it's also sent to
    /// the host, which raises it there for that player's proxy, so the host's
    /// enemies hear remote players too.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.alertInArea))]
    public static class CharacterAlertInArea
    {
        private static void Prefix(
            Vector3 _pos, float range, bool dangerousSound, float volume,
            bool gunshot, out CharBase __state)
        {
            var noise = new SentNoise
            {
                At = new Vec3(_pos.x, _pos.y, _pos.z),
                Range = range,
                Volume = volume,
                Dangerous = dangerousSound,
                Gunshot = gunshot,
            };

            __state = Plugin.Instance.Host.Noise.Raised(noise);
        }

        // A Finalizer rather than a Postfix: it runs when a listener's
        // heardSound throws too, which would otherwise leave NoiseSource set
        // and every later noise counted as this player's (and a client's never
        // sent).
        private static System.Exception Finalizer(
            System.Exception __exception, CharBase __state)
        {
            Plugin.Instance.Host.Noise.Done(__state);
            return __exception;
        }
    }

    /// <summary>
    /// Noise the game makes only for its own player, outside Player's code: a
    /// door the player opens or closes (Door alerts only with a Player as the
    /// opener), the player's melee hit on a door, window or barricade
    /// (MeleeSensor alerts only for type player), and furniture the player
    /// pushes or drags (ItemSounds checks Player.Instance). Counted as the
    /// player's noise; on a client it's sent to the host like their steps.
    /// </summary>
    [HarmonyPatch]
    static class OwnNoiseMakers
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(Door), nameof(Door.open),
                new[] { typeof(Vector3), typeof(Transform), typeof(float) });
            yield return AccessTools.Method(
                typeof(Door), nameof(Door.close), new[] { typeof(Transform) });
            yield return AccessTools.Method(
                typeof(MeleeSensor), "OnTriggerEnter");
            yield return AccessTools.Method(
                typeof(ItemSounds), nameof(ItemSounds.alertCharactersInArea));
        }

        static void Prefix(object __instance, object[] __args, out bool __state)
        {
            __state = Plugin.Instance.Host.Noise.EnterOwn(__instance, __args);
        }

        static System.Exception Finalizer(
            System.Exception __exception, bool __state)
        {
            Plugin.Instance.Host.Noise.LeaveOwn(__state);
            return __exception;
        }
    }
}
