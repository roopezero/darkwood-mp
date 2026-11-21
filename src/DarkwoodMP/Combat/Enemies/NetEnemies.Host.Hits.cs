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
    /// The host side: clients' hits on its enemies. See NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Host: an enemy made a sound; tell the clients that can see it.
        /// </summary>
        public void HostSound(Transform parent, string audioId, float volume)
        {
            var c = parent.GetComponentInParent<Character>();
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return;
            }

            foreach (var peer in Knowing(t.Id))
            {
                SendSound(peer, t.Id, audioId, volume, reliable: false);
            }
        }

        private void OnHit(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var hit = EnemyHitMessage.Read(r);
            uint id = hit.Id;
            float damage = hit.Damage;
            byte f = hit.Flags;
            var effects = PlayerDamage.GameOf(hit.Effects);
            string weaponJson = hit.Weapon;
            _byIdHost.TryGetValue(id, out var t);
            NetPlayerProxy.All.TryGetValue(from, out var proxy);
            var verdict = EnemyHitRules.MayHit(HitOf(t, proxy));
            if (verdict.Refused)
            {
                Coop.Current.Authority.Refuse(verdict);
                return;
            }

            ApplyHit(from, proxy, t.C, damage, f, effects, weaponJson);
        }

        /// <summary>
        /// Host, in a request's handler: a player's hit on one of our
        /// characters (an enemy, or an NPC that's still friendly here), checked
        /// (reach, hit rate) and capped by their weapon, applied with their
        /// stand-in as the attacker, so it reacts to them and gains threat for
        /// them.
        /// </summary>
        internal void ApplyHit(
            ulong from, NetPlayerProxy proxy, Character c, float damage, byte f,
            List<InvItemEffect> effects, string weaponJson)
        {
            var weapon = Weapon(weaponJson);
            var firing = FiringOf(weapon);
            float reach = EnemyHitRules.ReachFor(firing);
            if (!Coop.Current.Authority.InReach(
                c.transform.position, reach, c.name))
            {
                return;
            }

            var rate = _hitRate.Take(
                from, new Seconds(Time.realtimeSinceStartup), firing);
            if (rate.Refused)
            {
                Coop.Current.Authority.Refuse(rate);
                return;
            }

            float cap = Damage.WeaponCaps.Of(weapon);
            float applied = Damage.WeaponRules.Applied(damage, cap);
            if (Damage.WeaponRules.Cut(damage, applied))
            {
                var log = Plugin.Log;
                var hitter = _session.NameOf(from);
                var target = c.name;
                string weaponType = null;
                if (weapon != null)
                {
                    weaponType = weapon.type;
                }

                if (weaponType == null)
                {
                    weaponType = "bare hands";
                }

                log.LogInfo(
                    $"[authority] {hitter}'s hit on {target}: {damage:0} is "
                        + $"more than {weaponType} can do; {applied:0}");
            }

            Coop.Current.Authority.Result(
                applied.ToString(
                    "0", System.Globalization.CultureInfo.InvariantCulture));
            // The bar isn't shown for another player's hit (getHit would, as
            // byPlayer); as for any hit not by our player, it shows if we have
            // that enemy selected, and an open bar is refreshed.
            if (EnemyHitRules.Lands(applied))
            {
                // A death this hit causes sets off the character's onDeath as
                // that player's doing (World.ScriptedEvents.Cause). Its
                // onGetAttackedByPlayer triggers are the hitter's to set off
                // (their part, ours and the shared one, as any trigger of
                // theirs): left to them, and the result tells them
                // (ScriptedEvents.Attacked).
                var wasCause = World.ScriptedEvents.Cause;
                World.ScriptedEvents.Cause = from;
                int left;
                try
                {
                    left = ScriptedEvents.LeavingToActor(
                        ScriptedEvents.HitTypes, () =>
                        c.getHit(
                            applied, proxy.transform,
                            canCutInHalf: (f & 1) != 0, byPlayer: true,
                            canInterrupt: (f & 4) != 0, normalHit: (f & 8) != 0,
                            dontShowHealthBar: true));
                }
                finally
                {
                    World.ScriptedEvents.Cause = wasCause;
                }

                if (left > 0)
                {
                    Coop.Current.Authority.Result(
                        applied.ToString(
                            "0",
                            System.Globalization.CultureInfo.InvariantCulture)
                            + ScriptedEvents.Attacked);
                }

                if (c != null)
                {
                    HealthBar(
                        c,
                        show: Player.Instance != null
                            && Player.Instance.selectedObject == c.transform);
                }
            }

            if (c != null && c.effects != null)
            {
                foreach (var e in WeaponEffects(weapon, effects))
                {
                    // bleeding, burning... from their weapon
                    c.effects.activate(e);
                }
            }
        }

        private readonly Dictionary<InvItem, List<InvItemEffect>>
            _weaponEffects = new Dictionary<InvItem, List<InvItemEffect>>();

        /// <summary>
        /// Of the effects a hit came with, those the weapon's class can cause,
        /// with the class's own numbers rather than the sent ones: its melee
        /// sensors' effects (MeleeSensor.effects) and its flames'
        /// (Flame.effect: a firearm's shot is its `item` prefab, which
        /// Player.spawnBullet spawns, a flamethrower's flame among them; also
        /// its muzzle and particle prefabs). Bare hands cause none.
        /// </summary>
        private List<InvItemEffect> WeaponEffects(
            InvItemClass weapon, List<InvItemEffect> sent)
        {
            var result = new List<InvItemEffect>();
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null || sent == null || sent.Count == 0)
            {
                return result;
            }

            if (!_weaponEffects.TryGetValue(b, out var own))
            {
                own = new List<InvItemEffect>();
                UnityEngine.Object shot = null;
                if (b.isFirearm)
                {
                    shot = b.item;
                }

                foreach (var o in new[]
                {
                    b.meleeSensor,
                    b.meleeSensorSpecial,
                    shot,
                    b.muzzlePrefab,
                    b.muzzleParticles,
                    b._particleEmitter
                })
                {
                    GameObject go = o as GameObject;
                    if (go == null)
                    {
                        Component component = o as Component;
                        if (component != null)
                        {
                            go = component.gameObject;
                        }
                    }

                    if (go == null)
                    {
                        continue;
                    }

                    foreach (var ms in go.GetComponentsInChildren<MeleeSensor>(
                        true))
                    {
                        if (ms.effects != null)
                        {
                            foreach (var e in ms.effects)
                            {
                                if (e != null)
                                {
                                    own.Add(e);
                                }
                            }
                        }
                    }

                    foreach (var fl in go.GetComponentsInChildren<Flame>(true))
                    {
                        if (fl.effect != null)
                        {
                            own.Add(fl.effect);
                        }
                    }
                }

                _weaponEffects[b] = own;
            }

            var picked = EnemyHitRules.Effects(new EffectFacts
            {
                Armed = true,
                Own = own.Select(e => (int)e.type).ToArray(),
                Sent = sent.Select(e => (int)e.type).ToArray(),
            });

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i] < 0)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] a hit with {weapon.type} came with "
                            + $"{sent[i].type}, which it doesn't cause; "
                            + "dropped");
                    continue;
                }

                result.Add(own[picked[i]]);
            }

            return result;
        }

        /// <summary>
        /// How far a melee hit reaches (generous: the positions the host has
        /// are a little late).
        /// </summary>
        private readonly HitRate _hitRate = new HitRate();

        /// <summary>
        /// The checks' facts for a hit on a tracked enemy by a player's
        /// stand-in.
        /// </summary>
        private static HitFacts HitOf(Tracked t, NetPlayerProxy proxy)
        {
            bool tracked = t != null && t.C != null;
            return new HitFacts
            {
                Tracked = tracked,
                InPlay = tracked && InPlay(t.C),
                StandIn = proxy != null,
            };
        }

        /// <summary>
        /// What the hitter's weapon reaches and how fast it hits; bare hands
        /// for none.
        /// </summary>
        private static FiringFacts FiringOf(InvItemClass weapon)
        {
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null)
            {
                return new FiringFacts { Armed = false };
            }

            return new FiringFacts
            {
                Armed = true,
                Melee = b.isMelee,
                Firearm = b.isFirearm,
                Auto = b.fireMode == InvItem.FireMode.fullAuto
                    || b.fireMode == InvItem.FireMode.burst,
                FireRate = b.fireRate,
                Projectiles = b.projectileAmount,
                Type = weapon.type,
            };
        }

        private static InvItemClass Weapon(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var st =
                    Newtonsoft.Json.JsonConvert
                        .DeserializeObject<InvItemClass.SaveState>(json);
                var it = new InvItemClass();
                st.loadValues(it);
                if (InvItemClass.isNull(it) || it.baseClass == null)
                {
                    return null;
                }

                return it;
            }
            catch
            {
                return null;
            }
        }
    }
}
