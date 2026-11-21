using ClientHitFacts = DarkwoodMP.Combat.Damage.ClientHitFacts;
using ClientHitRules = DarkwoodMP.Combat.Damage.ClientHitRules;
using HitWay = DarkwoodMP.Combat.Damage.ClientHit;
using HostEffectsFacts = DarkwoodMP.Combat.Damage.HostEffectsFacts;
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
    /// The client side: our hits on the host's enemies, and the game's
    /// calls that ClientHitPatches hands on. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Client: we hit a host's enemy; the host applies it.
        /// </summary>
        public void ClientHit(
            uint id, float damage, bool cut, bool byPlayer, bool interrupt,
            bool normal, List<InvItemEffect> effects)
        {
            int flags = 0;
            if (cut)
            {
                flags |= EnemyHitMessage.Cut;
            }

            if (byPlayer)
            {
                flags |= EnemyHitMessage.ByPlayer;
            }

            if (interrupt)
            {
                flags |= EnemyHitMessage.Interrupt;
            }

            if (normal)
            {
                flags |= EnemyHitMessage.Normal;
            }

            InvItemClass item = null;
            if (Player.Instance != null)
            {
                item = Player.Instance.currentItem;
            }

            string s;
            if (InvItemClass.isNull(item))
            {
                s = "";
            }
            else
            {
                s = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new InvItemClass.SaveState(item, 0));
            }

            var w = _session.Begin(MsgType.EnemyHit);
            EnemyHitMessage.Write(w, new EnemyHitRequest
            {
                Id = id,
                Damage = damage,
                Flags = (byte)flags,
                Effects = PlayerDamage.SentOf(effects),
                Weapon = s,
            });

            _puppets.TryGetValue(id, out var p);
            Character hit = null;
            if (p != null)
            {
                hit = p.C;
            }

            Coop.Current.Authority.Request(
                w, $"hit on enemy #{id} for {damage:0}",
                (ok, reason, result) =>
            {
                if (ok)
                {
                    Coop.Current.Scripted.HitApplied(hit, result);
                }
            });

            // Our own hit shows its health bar, as getHit does; the host's
            // health for it follows in its next state.
            if (p == null || p.C == null)
            {
                return;
            }

            if (CopyRules.ShowsBar(damage))
            {
                HealthBar(p.C, show: true);
            }

            // Predicted: the blood shows at once; the host's for this hit (a
            // moment later) isn't shown again.
            Core.AddPrefab(
                CopyRules.BloodFor(false), p.C.transform.position,
                Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f),
                null);
            _predictedBlood[id] = Time.realtimeSinceStartup;
        }

        private readonly Dictionary<uint, float> _predictedBlood =
            new Dictionary<uint, float>();

        /// <summary>
        /// Client: the effects of the weapon hitting right now (set around a
        /// player's MeleeSensor hit), the session's: with no session it reads
        /// null and keeps nothing.
        /// </summary>
        public static List<InvItemEffect> HitEffects
        {
            get
            {
                NetEnemies enemies = Instance;
                if (enemies == null)
                {
                    return null;
                }

                return enemies._hitEffects;
            }

            set
            {
                NetEnemies enemies = Instance;
                if (enemies != null)
                {
                    enemies._hitEffects = value;
                }
            }
        }

        private List<InvItemEffect> _hitEffects;

        /// <summary>
        /// Character.getHit runs here: not for a copy of the host's enemy or
        /// a frozen one of ours. Our player's hit on a copy goes to the host
        /// (EnemyHit); one on a friendly NPC is asked of the host's copy.
        /// </summary>
        public bool TakesHit(Character c, in GameHit hit)
        {
            uint id = 0;
            bool puppet = ClientMode && IsPuppet(c, out id);
            ClientHitFacts facts = HitOf(c, hit, puppet);
            switch (ClientHitRules.Forward(facts))
            {
                case HitWay.Game:
                    return true;
                case HitWay.AskNpcs:
                    // A friendly NPC our player hits: the host's copy takes
                    // it, and turns on us there.
                    return !HostileNpcs.ClientHit(
                        c, hit.Damage, hit.Attacker, hit.CanCutInHalf,
                        hit.ByPlayer, hit.CanInterrupt, hit.NormalHit);
                case HitWay.ToHost:
                    // Only what our own player does to it is ours to send.
                    ClientHit(
                        id, hit.Damage, hit.CanCutInHalf,
                        ClientHitRules.FromPlayer(facts), hit.CanInterrupt,
                        hit.NormalHit, _hitEffects);
                    return false;
                default:
                    // A frozen copy isn't really there. A flame of no damage
                    // can still set it alight (its burn goes with the hit),
                    // so only a hit of no damage and no effects stops here.
                    // Something thrown hits it in the host's copy of the
                    // throw; of flames only our own weapon's are ours to
                    // send (Flame calls every hit byPlayer); anything else
                    // not by our player (a trap, fire, an explosion)
                    // happened on the host too, where it hurt the enemy
                    // already.
                    return false;
            }
        }

        private ClientHitFacts HitOf(Character c, in GameHit hit, bool puppet)
        {
            if (!ClientMode)
            {
                return new ClientHitFacts { Handled = false };
            }

            int effects = 0;
            if (_hitEffects != null)
            {
                effects = _hitEffects.Count;
            }

            var flame = World.ThrownHits.CurrentFlame;
            bool ours = false;
            if (flame != null)
            {
                ours = EnemyAreaHits.ByLocalPlayer(flame);
            }

            Transform attacker = hit.Attacker;
            return new ClientHitFacts
            {
                Handled = true,
                Puppet = puppet,
                Frozen = _frozen.Contains(c),
                Damage = hit.Damage,
                Effects = effects,
                Thrown = World.ThrownHits.Now(),
                Flame = flame != null,
                OurFlame = ours,
                ByPlayer = hit.ByPlayer,
                PlayerAttacker = attacker != null
                    && attacker.GetComponent<Player>() != null,
            };
        }

        /// <summary>
        /// A melee sensor touches something: our player's weapon's effects
        /// are noted, to go with a hit on a copy (EnemyHit).
        /// </summary>
        public void MeleeTouches(MeleeSensor sensor)
        {
            bool player = sensor.type == MeleeSensor.MeleeSensorType.player;
            if (ClientHitRules.NotesEffects(ClientMode, player))
            {
                _hitEffects = sensor.effects;
            }
        }

        /// <summary>
        /// The melee sensor's touch is over, or threw.
        /// </summary>
        public void MeleeTouched()
        {
            _hitEffects = null;
            HostileNpcs.EndHit();
        }

        /// <summary>
        /// CharacterEffects.activate runs: not on a copy of the host's enemy
        /// or a frozen one of ours (the host's effects come with its hit).
        /// </summary>
        public bool RunsEffects(CharacterEffects effects)
        {
            return ClientHitRules.RunsEffects(EffectsOf(effects));
        }

        private HostEffectsFacts EffectsOf(CharacterEffects effects)
        {
            if (!ClientMode)
            {
                return new HostEffectsFacts { Handled = false };
            }

            var c = effects.GetComponent<Character>();
            if (c == null)
            {
                return new HostEffectsFacts { Handled = true };
            }

            return new HostEffectsFacts
            {
                Handled = true,
                Character = true,
                Puppet = IsPuppet(c, out _),
                Frozen = _frozen.Contains(c),
                Forwarded = HostileNpcs.EffectsForwarded(c),
            };
        }
    }
}
