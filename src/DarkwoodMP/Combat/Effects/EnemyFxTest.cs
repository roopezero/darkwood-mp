using DarkwoodMP.Combat.Effects;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// "--dwmp-test-blast" (host), after AreaHitsTest: the nearest enemy to a
    /// joined player screams as a banshee does (FX/BansheeScream, sent as
    /// EnemyEffect), the forest spirit's effect is made beside the player
    /// (pooled, sent as EnemyEffect), and the host's after-night is ended
    /// (AfterNightEnded).
    /// </summary>
    static class EnemyFxTest
    {
        internal static System.Collections.IEnumerator Run()
        {
            NetPlayerProxy p;
            while ((p = AreaHitsTest.Joined()) == null)
            {
                yield return null;
            }

            yield return new WaitForSeconds(8f);
            if (p == null)
            {
                yield break;
            }

            var at = p.transform.position;
            var c = AreaHitsTest.NearestEnemy(at);
            if (c != null)
            {
                Plugin.Log.LogInfo(
                    $"--dwmp-test-blast: {c.name} screams as a banshee");
                try
                {
                    c.bansheeScream();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogInfo(
                        "--dwmp-test-blast: the scream failed here: "
                            + $"{e.Message}");
                }
            }

            Plugin.Log.LogInfo(
                "--dwmp-test-blast: the forest spirit's effect beside "
                    + $"{p.OwnerName}");
            try
            {
                Core.AddPooledPrefab(
                    "FX", EffectRules.ForestSpirit,
                    at + new Vector3(100f, 0f, 0f), Quaternion.identity);
            }
            catch (System.Exception e)
            {
                // Chapter 1's FX pool hasn't got it: send it anyway, for the
                // joiner's side of the message.
                Plugin.Log.LogInfo(
                    "--dwmp-test-blast: no such pooled effect here "
                        + $"({e.Message}): sent without the patch");
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.EnemyFx.SendPrefab(
                        EffectRules.PooledKind, EffectRules.ForestSpirit,
                        at + new Vector3(100f, 0f, 0f), 0f);
                }
            }

            yield return new WaitForSeconds(1f);
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null)
            {
                Plugin.Log.LogInfo(
                    "--dwmp-test-blast: the host's after-night ends");
                ctl.isAfterNight = true;
                ctl.endAfterNight();
            }

            Plugin.Log.LogInfo("--dwmp-test-blast: effects done");
        }
    }
}
