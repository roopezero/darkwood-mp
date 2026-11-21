using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side: the shadow armour the host's enemies wear. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Client: EnemyArmor, the host's enemy's shadow armour put on or gone.
        /// </summary>
        private void OnArmor(ulong from, PacketReader r)
        {
            if (!FreezeRules.FromHost(from == _session.HostId, ClientMode))
            {
                return;
            }

            var mark = EnemyMarkMessage.Read(r);
            uint id = mark.Id;
            bool on = mark.On;
            if (_puppets.TryGetValue(id, out var p) && p.C != null)
            {
                SetArmor(p.C, on, fade: true);
            }
        }

        /// <summary>
        /// Client: a copy wears shadow armour as the host's enemy does. The
        /// armour only shows its effect: its light check and damage do
        /// nothing on a copy (ShadowArmorDamageMe), and it goes when the
        /// host's does (EnemyArmor), fading as the game's ShadowArmor.die makes
        /// it. A copy from our save may wear armour the host's no longer does;
        /// that goes at once.
        /// </summary>
        internal static void SetArmor(Character c, bool on, bool fade)
        {
            try
            {
                var a = c.GetComponent<ShadowArmor>();
                bool has = a != null;
                var step = CopyRules.Armor(
                    on, has, has && a.effectGO != null);
                if (step == ArmorStep.Add)
                {
                    // its Start makes the effect
                    c.shadowArmor = c.gameObject.AddComponent<ShadowArmor>();
                    Plugin.Log.LogInfo(
                        $"[enemy] {c.name}: shadow armour shown");
                }
                else if (step != ArmorStep.None)
                {
                    if (step == ArmorStep.Destroy)
                    {
                        Object.Destroy(a); // not started yet: no effect to stop
                    }
                    else
                    {
                        a.die(instant: !fade);
                    }

                    Plugin.Log.LogInfo($"[enemy] {c.name}: shadow armour gone");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't change {c.name}'s shadow armour: {e.Message}");
            }
        }

        /// <summary>
        /// Client: a ShadowArmor on a copy of the host's enemy, or on one of
        /// our own frozen characters.
        /// </summary>
        internal static bool IsCopy(ShadowArmor a)
        {
            var s = Plugin.Instance.Host.Session;
            if (a == null || s == null)
            {
                return false;
            }

            var c = a.GetComponent<Character>();
            NetEnemies enemies = Instance;
            if (c == null || enemies == null)
            {
                return false;
            }

            return FreezeRules.IsCopy(
                s.IsClient,
                enemies._frozen.Contains(c),
                enemies._byChar.ContainsKey(c));
        }
    }
}
