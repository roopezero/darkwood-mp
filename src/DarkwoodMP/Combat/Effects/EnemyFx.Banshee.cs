using System.Collections.Generic;
using DarkwoodMP.Combat.Effects;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// EnemyFx on a client: the banshee's sight, as the host's banshee
    /// looks at our player.
    internal sealed partial class EnemyFx
    {
        // ---- the banshee's sight (client) -----------------------------------

        private readonly HashSet<Character> _seen =
            new HashSet<Character>();

        private void BansheeSight()
        {
            var me = Player.Instance;
            NetEnemies e = _coop.Enemies;
            if (me == null || e == null)
            {
                return;
            }

            bool away = EnemyAreaHits.PlayerAway();
            var now = new List<Character>();
            var distances = new List<float>();
            foreach (var c in e.PuppetCharacters())
            {
                if (!BansheeRules.Sees(FactsOf(c, me, away)))
                {
                    continue;
                }

                if (now.Contains(c))
                {
                    continue;
                }

                now.Add(c);
                distances.Add(BansheeRules.Distance(
                    Core.trueDistance(
                        c.transform.position, me.transform.position)));
            }

            // Out of sight now: as onBansheeOutOfSightOfPlayer does for its
            // look.
            foreach (var c in _seen)
            {
                if (c != null && !now.Contains(c))
                {
                    SightLight(c, false);
                }
            }

            if (now.Count == 0)
            {
                if (_seen.Count > 0)
                {
                    Unseen(me);
                }

                _seen.Clear();
                return;
            }

            int i = BansheeRules.Nearest(distances.ToArray());
            Character nearest = null;
            float nearestDist = float.MaxValue;
            if (i >= 0)
            {
                nearest = now[i];
                nearestDist = distances[i];
            }

            if (_seen.Count == 0)
            {
                string name = "?";
                if (nearest != null)
                {
                    name = nearest.name;
                }

                Plugin.Log.LogInfo(
                    "[enemyfx] our player sees the host's banshee "
                        + $"{name} ({nearestDist:0} away)");
            }

            _seen.Clear();
            foreach (var c in now)
            {
                _seen.Add(c);
                SightLight(c, true);
            }

            Seen(me, nearestDist);
        }

        /// <summary>
        /// One of the host's banshees and our player: the only code reading
        /// them for BansheeRules.Sees. Its sight is read only when the rest
        /// lets it, as the game does.
        /// </summary>
        private static BansheeFacts FactsOf(Character c, Player me, bool away)
        {
            var facts = new BansheeFacts
            {
                Banshee = c.banshee,
                Away = away,
                Alive = me.alive,
            };

            if (!facts.Banshee || facts.Away || !facts.Alive)
            {
                return facts;
            }

            bool far = c.inSightOfPlayer != null
                && c.inSightOfPlayer.playerCanBeFarAway;
            return facts with { InSight = me.isInSight(c.transform, far) };
        }

        /// <summary>
        /// As onBansheeSeePlayer and bansheeAgitated do, for the nearest
        /// one.
        /// </summary>
        private static void Seen(Player me, float nearestDist)
        {
            if (me.bansheeAgitatedSoundAO == null)
            {
                me.bansheeAgitatedSoundAO = AudioController.Play(
                    BansheeRules.AgitatedSound, me._transform);
            }

            CamMain instance = Singleton<CamMain>.Instance;
            if (instance != null)
            {
                instance.shake(
                    BansheeRules.ShakeTime, BansheeRules.Shake(nearestDist));
            }

            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                ui.initBansheeOverlay();
                if (ui.bansheeOverlay != null)
                {
                    Core.tweenAlpha(
                        ui.bansheeOverlay.gameObject,
                        BansheeRules.Overlay(nearestDist),
                        BansheeRules.OverlayTime, timeScaleDependent: true);
                }
            }
        }

        private static void Unseen(Player me)
        {
            if (me.bansheeAgitatedSoundAO != null)
            {
                me.bansheeAgitatedSoundAO.Stop(1f);
                me.bansheeAgitatedSoundAO = null;
            }

            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                ui.initBansheeOverlay();
                if (ui.bansheeOverlay != null)
                {
                    Core.tweenAlpha(
                        ui.bansheeOverlay.gameObject, 0f, 0.5f,
                        timeScaleDependent: true);
                }

                ui.wantToRemoveBansheeOverlay();
            }

            Plugin.Log.LogInfo(
                "[enemyfx] the host's banshees are out of our player's sight");
        }

        private static void SightLight(Character c, bool on)
        {
            var t = c.transform.Find("SightLight");
            if (t != null && t.gameObject.activeSelf != on)
            {
                t.gameObject.SetActive(on);
            }
        }
    }
}
