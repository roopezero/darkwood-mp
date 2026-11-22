using System;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    static partial class JoinerAi
    {
        /// <summary>
        /// Part of "--dwmp-test-night" (after the night attacker): on character
        /// c and a proxy, checks the patches here by calling the game's methods
        /// as it would. Logs "[joiner ai test]" lines.
        /// </summary>
        public static void Test(Character c, NetPlayerProxy proxy)
        {
            if (c == null || proxy == null)
            {
                Plugin.Log.LogInfo("[joiner ai test] nothing to test on");
                return;
            }

            string T(Transform t)
            {
                if (t == null)
                {
                    return "nobody";
                }

                if (t == proxy.transform)
                {
                    return proxy.OwnerName;
                }

                return t.name;
            }

            try
            {
                foreach (var a in c.activities)
                {
                    if (a == null || !(a.getFromLocation || a.playerIsTarget))
                    {
                        continue;
                    }

                    a.thisCharacter = c;
                    a.assignTarget();
                    Location loc = null;
                    if (a.target != null)
                    {
                        loc = Plugin.Instance.Host.Nights.BigLocationAt(
                            a.target.transform.position);
                    }

                    string targetName = "none";
                    if (a.target != null)
                    {
                        targetName = a.target.name;
                    }

                    string locationName = "no location";
                    if (loc != null)
                    {
                        locationName = loc.name;
                    }

                    Plugin.Log.LogInfo(
                        $"[joiner ai test] activity {a.type}: target "
                            + $"{targetName}, in {locationName}");
                }

                Plugin.Log.LogInfo(
                    $"[joiner ai test] {c.name} has {c.activities.Count} "
                        + "activities");

                c.transform.position =
                    proxy.transform.position + new Vector3(800f, 0f, 0f);
                bool near = c.inSightOrCloseToPlayer();
                float distance = Core.trueDistance(
                    c.transform.position, Player.Instance.transform.position);
                Plugin.Log.LogInfo(
                    $"[joiner ai test] near the joiner: {near}, "
                        + $"{distance:0} from the host");

                var onCollide =
                    AccessTools.Method(typeof(Character), "onCollideWith");
                c.sleeping = true;
                c.wakingUp = false;
                onCollide.Invoke(
                    c, new object[] { proxy.GetComponent<Collider>() });
                Plugin.Log.LogInfo(
                    $"[joiner ai test] bump on a sleeping {c.name}: waking up "
                        + $"{c.wakingUp}");
                c.sleeping = false;
                c.wakingUp = false;

                c.stopAttacking(proxy.transform);
                c.cowardiness = Cowardiness.always;
                if (c.aggressiveness == Aggressiveness.flee
                    || c.aggressiveness == Aggressiveness.fleeAndDespawn)
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                AccessTools.Method(typeof(Character), "reactToCharacter")
                    .Invoke(c, new object[] { null, proxy.transform, true });
                Plugin.Log.LogInfo(
                    "[joiner ai test] hit by the joiner (cowardiness always): "
                        + $"behaviour {c.behaviour}, target {T(c.target)}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[joiner ai test] failed: {e}");
            }
        }
    }
}
