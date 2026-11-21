using System.Collections.Generic;
using System.Linq;
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

            uint id = r.R.ReadUInt32();
            float damage = r.R.ReadSingle();
            byte f = r.R.ReadByte();
            var effects = PlayerDamage.ReadEffects(r);
            string weaponJson = r.ReadStr();
            _byIdHost.TryGetValue(id, out var t);
            if (t == null || t.C == null || !InPlay(t.C))
            {
                Coop.Current.Authority.Refuse("that enemy isn't alive here");
                return;
            }

            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy)
                || proxy == null)
            {
                Coop.Current.Authority.Refuse(
                    "no stand-in for that player yet");
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
            bool ranged = weapon != null && weapon.baseClass != null
                && !weapon.baseClass.isMelee;
            float reach;
            if (ranged)
            {
                reach = RelevantRange;
            }
            else
            {
                reach = MeleeReach;
            }

            if (!Coop.Current.Authority.InReach(
                c.transform.position, reach, c.name))
            {
                return;
            }

            if (!HitRateOk(from, weapon))
            {
                string type = null;
                if (weapon != null)
                {
                    type = weapon.type;
                }

                if (type == null)
                {
                    type = "bare hands";
                }

                Coop.Current.Authority.Refuse(
                    $"more hits a second than {type} makes");
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
            if (applied > 0f)
            {
                // A death this hit causes sets off the character's onDeath as
                // that player's doing (World.ScriptedEvents.Cause). Its
                // onGetAttackedByPlayer triggers are the hitter's to set off
                // (their part, ours and the shared one, as any trigger of
                // theirs): left to them, and the result tells them
                // (ScriptedEvents.Attacked).
                var scripted = Coop.Current.Scripted;
                var wasCause = scripted.Cause;
                scripted.Cause = from;
                int left;
                try
                {
                    left = scripted.LeavingToActor(
                        ScriptedEvents.HitTypes, () =>
                        c.getHit(
                            applied, proxy.transform,
                            canCutInHalf: (f & 1) != 0, byPlayer: true,
                            canInterrupt: (f & 4) != 0, normalHit: (f & 8) != 0,
                            dontShowHealthBar: true));
                }
                finally
                {
                    scripted.Cause = wasCause;
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

            var left = new List<InvItemEffect>(own);
            foreach (var e in sent)
            {
                int i = left.FindIndex(x => x.type == e.type);
                if (i < 0)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] a hit with {weapon.type} came with "
                            + $"{e.type}, which it doesn't cause; dropped");
                    continue;
                }

                result.Add(left[i]);
                left.RemoveAt(i);
            }

            return result;
        }

        /// <summary>
        /// How far a melee hit reaches (generous: the positions the host has
        /// are a little late).
        /// </summary>
        private const float MeleeReach = 500f;
        /// <summary>
        /// Most hits a player lands in a second with a melee weapon or bare
        /// hands (or a slow gun).
        /// </summary>
        private const int MaxHitsPerSecond = 24;
        private readonly Dictionary<ulong, Queue<float>> _hitTimes =
            new Dictionary<ulong, Queue<float>>();

        /// <summary>
        /// Most hits a second this weapon lands, by the host's numbers for its
        /// type: each of a gun's shots sends a hit for every pellet that lands
        /// (InvItem.projectileAmount, Player.fireWeapon), and it fires at most
        /// once per fireRate seconds (at most every 0.05 s for automatic and
        /// burst fire, 0.15 s for one shot a click), with half again as a
        /// margin for hits that arrive bunched up. Never under
        /// MaxHitsPerSecond.
        /// </summary>
        private static int MaxHits(InvItemClass weapon)
        {
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null || !b.isFirearm)
            {
                return MaxHitsPerSecond;
            }

            // A gun fired one shot per click (oneShot, semiAuto) has its
            // fireRate at 0 or near it: its pump or bolt animation, and the
            // clicks, keep it under about 7 shots a second.
            bool auto = b.fireMode == InvItem.FireMode.fullAuto
                || b.fireMode == InvItem.FireMode.burst;
            float minRate = 0.15f;
            if (auto)
            {
                minRate = 0.05f;
            }

            float shots = 1f / Mathf.Max(b.fireRate, minRate);
            return Mathf.Max(
                MaxHitsPerSecond,
                Mathf.CeilToInt(
                    Mathf.Max(1, b.projectileAmount) * shots * 1.5f));
        }

        private bool HitRateOk(ulong from, InvItemClass weapon)
        {
            if (!_hitTimes.TryGetValue(from, out var q))
            {
                _hitTimes[from] = q = new Queue<float>();
            }

            float now = Time.realtimeSinceStartup;
            while (q.Count > 0 && now - q.Peek() > 1f)
            {
                q.Dequeue();
            }

            if (q.Count >= MaxHits(weapon))
            {
                return false;
            }

            q.Enqueue(now);
            return true;
        }

        /// <summary>
        /// The weapon a hit came with (InvItemClass.SaveState as JSON; null for
        /// bare hands or one we can't read).
        /// </summary>
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
