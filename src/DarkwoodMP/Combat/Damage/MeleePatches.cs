using DarkwoodMP.Combat.Damage;
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
    public static class MeleeSensorHitsStandIn
    {
        private static bool Prefix(MeleeSensor __instance, Collider _collider)
        {
            NetPlayerProxy proxy = null;
            if (_collider != null)
            {
                proxy = _collider.GetComponent<NetPlayerProxy>();
            }

            MeleeFacts touch = FactsOf(__instance, _collider, proxy);
            MeleeOutcome outcome = MeleeRules.Touch(touch);
            if (outcome == MeleeOutcome.Game)
            {
                return true;
            }

            // Only the colliders: the game clears them as the pooled sensor is
            // spawned again (MeleeSensor.OnSpawned), but never its
            // gameObjectsToIgnore, so a pooled sensor would never hit the same
            // stand-in twice.
            var s = __instance;
            if (outcome == MeleeOutcome.Unseen || outcome == MeleeOutcome.Hits)
            {
                s.collidersToIgnore.Add(_collider);
            }

            if (outcome == MeleeOutcome.Unseen)
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

            if (outcome != MeleeOutcome.Hits)
            {
                return false; // players don't hit players, or hit already
            }

            if (DogWatch())
            {
                Plugin.Log.LogInfo(
                    $"[dogwatch] {s.attackerTransform.name} hit "
                        + $"{proxy.OwnerName}'s stand-in for {s.damage:0}");
            }

            float damage = MeleeRules.Damage(touch);
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
        /// The touch as the rule sees it. The sight check runs only when
        /// the game's would: an enemy's sensor with an attacker, a stand-in
        /// not hit before, and a sensor that needs line of sight.
        /// </summary>
        private static MeleeFacts FactsOf(
            MeleeSensor s, Collider collider, NetPlayerProxy proxy)
        {
            if (proxy == null)
            {
                return new MeleeFacts { StandIn = false };
            }

            bool enemy = s.type == MeleeSensor.MeleeSensorType.character;
            bool attacker = s.attackerTransform != null;
            bool already = s.collidersToIgnore.Contains(collider);
            bool sees = false;
            if (enemy && attacker && !already && !s.doesNotNeedLineOfSight)
            {
                sees = Core.canSeeAttack(s.attackerTransform, proxy.transform);
            }

            bool strength = false;
            float modifier = 1f;
            if (attacker)
            {
                var c = s.attackerTransform.GetComponent<CharBase>();
                if (c != null)
                {
                    strength = true;
                    modifier = c.strengthModifier;
                }
            }

            return new MeleeFacts
            {
                StandIn = true,
                EnemySensor = enemy,
                Attacker = attacker,
                AlreadyHit = already,
                NoSightNeeded = s.doesNotNeedLineOfSight,
                Sees = sees,
                Damage = s.damage,
                Strength = strength,
                Modifier = modifier,
            };
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
