using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// An enemy's attack is a short-lived MeleeSensor trigger; the game applies
    /// it to a Character or a Player it touches. A proxy is neither, so its
    /// hits are handled here: the same checks the game makes for a player (not
    /// ignored, the attacker can see it), then the damage and effects go to the
    /// proxy's player.
    /// </summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    public static class MeleeSensorProxyPatch
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            if (_collider == null)
            {
                return true;
            }

            var proxy = _collider.GetComponent<NetPlayerProxy>();
            if (proxy == null)
            {
                return true;
            }

            var s = __instance;
            if (s.type != MeleeSensor.MeleeSensorType.character
                || s.attackerTransform == null)
            {
                return false; // players don't hit players
            }

            // Only the colliders: the game clears them as the pooled sensor is
            // spawned again (MeleeSensor.OnSpawned), but never its
            // gameObjectsToIgnore, so a pooled sensor would never hit the same
            // stand-in twice.
            if (s.collidersToIgnore.Contains(_collider))
            {
                return false;
            }

            s.collidersToIgnore.Add(_collider);
            if (!s.doesNotNeedLineOfSight
                && !Core.canSeeAttack(s.attackerTransform, proxy.transform))
            {
                if (DogWatch())
                {
                    Plugin.Log.LogInfo(
                        $"[dogwatch] {s.attackerTransform.name}'s hit on "
                            + $"{proxy.OwnerName}'s stand-in blocked: no line "
                            + "of sight");
                }

                return false;
            }

            if (DogWatch())
            {
                Plugin.Log.LogInfo(
                    $"[dogwatch] {s.attackerTransform.name} hit "
                        + $"{proxy.OwnerName}'s stand-in for {s.damage:0}");
            }

            var attacker = s.attackerTransform.GetComponent<CharBase>();
            float strength = 1f;
            if (attacker != null)
            {
                strength = attacker.strengthModifier;
            }

            float damage = s.damage * strength;
            meleeSensorDelegate handler = s.onHit;
            if (handler != null)
            {
                handler();
            }

            PlayerDamage.Send(
                proxy.Owner, damage, s.attackerTransform, s.shadowSensor,
                s.effects);
            return false;
        }

        /// <summary>
        /// Whether the map test's dog-watch step wants these hits logged.
        /// </summary>
        private static bool DogWatch()
        {
            ITestSteps steps = Plugin.Instance.TestSteps;
            return steps != null && steps.DogWatch;
        }
    }
}
