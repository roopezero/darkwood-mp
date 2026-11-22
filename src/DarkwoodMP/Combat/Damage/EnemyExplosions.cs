using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Combat.Effects;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: the host's enemies' explosions happen only in the host's game
    /// (EnemyAreaHits), so the host sends each one's sound and flash as
    /// EnemyExplosion, and they are made here, as a replay, when our player
    /// is near.
    /// </summary>
    internal sealed class EnemyExplosions
    {
        private readonly CoopSession _coop;

        public EnemyExplosions(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.EnemyExplosion, OnExplosion);
        }

        private readonly Dictionary<string, GameObject> _fxByName =
            new Dictionary<string, GameObject>();

        private void OnExplosion(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (!s.IsClient || from != s.HostId
                || !WorldShare.InWorld)
            {
                return;
            }

            var blast = EnemyExplosionMessage.Read(r);
            var pos = new Vector3(blast.At.X, blast.At.Y, blast.At.Z);
            string sound = blast.Sound;
            string fx = blast.Effect;
            var me = Player.Instance;
            float d = 0f;
            if (me != null)
            {
                d = Core.trueDistance(me.transform.position, pos);
            }

            if (!EffectRules.Shown(me != null, EnemyAreaHits.PlayerAway(), d))
            {
                return;
            }

            if (sound.Length > 0)
            {
                AudioController.Play(sound, pos);
            }

            var prefab = Fx(fx);
            if (prefab == null)
            {
                return;
            }

            // Only the look: anything in it that could hurt counts as part of
            // an explosion (the host already dealt it).
            Coop.Guards.Replaying = true;
            ThrownHits.Depth++;
            try
            {
                Core.AddPrefab(
                    prefab, pos, Quaternion.Euler(90f, 0f, 0f), null,
                    worldSpace: true);
            }
            finally
            {
                ThrownHits.Depth--;
                Coop.Guards.Replaying = false;
            }
        }

        /// <summary>
        /// An explosion prefab by name, among the assets this game has loaded
        /// (the enemy that throws it is here too).
        /// </summary>
        private GameObject Fx(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (_fxByName.TryGetValue(name, out var go) && go != null)
            {
                return go;
            }

            go = null;
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g != null && g.name == name && !g.scene.IsValid()
                    && g.transform.parent == null)
                {
                    go = g;
                    break;
                }
            }

            if (go == null)
            {
                Plugin.Log.LogInfo(
                    $"The host's enemy exploded with {name}, which this game "
                        + "hasn't loaded: sound only");
            }

            _fxByName[name] = go;
            return go;
        }
    }
}
