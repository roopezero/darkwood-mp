using ClientHitFacts = DarkwoodMP.Combat.Damage.ClientHitFacts;
using ClientHitRules = DarkwoodMP.Combat.Damage.ClientHitRules;
using HitWay = DarkwoodMP.Combat.Damage.ClientHit;
using HostEffectsFacts = DarkwoodMP.Combat.Damage.HostEffectsFacts;
using DarkwoodMP.Combat.Enemies;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side's answers to the game's calls that FreezePatches and
    /// ClientHitPatches hand on: a frozen copy never acts. See NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Character.enableComponents(enable) runs: not for a frozen one of
        /// ours as its area is switched on again. A copy of the host's enemy
        /// plays the host's clips on its sprite animator, which
        /// enableComponents(false) (Location.activateCharacters as its area
        /// was left) switched off: that one is switched back on.
        /// </summary>
        public bool EnablesComponents(Character c, bool enable)
        {
            bool client = enable && ClientMode;
            if (!FreezeRules.StaysFrozen(
                enable,
                client,
                client && _frozen.Contains(c)))
            {
                return true;
            }

            if (IsPuppet(c, out _))
            {
                var a = c.animator;
                if (a != null)
                {
                    a.enabled = true;
                }
            }

            return false;
        }

        /// <summary>
        /// CharacterSounds.playIdleLoop(loop) runs: a hidden one of ours
        /// starts no loop; stopping a loop ("") still runs.
        /// </summary>
        public bool PlaysIdleLoop(CharacterSounds sounds, string loop)
        {
            if (string.IsNullOrEmpty(loop) || _hidden.Count == 0)
            {
                return true;
            }

            var c = sounds.GetComponent<Character>();
            return !FreezeRules.StaysSilent(
                loop,
                ClientMode,
                c != null && _hidden.Contains(c));
        }

        /// <summary>
        /// CharacterSpawnPoint.actuallySpawn runs: not in the host's world,
        /// or the spawn point is in an area only we are in.
        /// </summary>
        public bool SpawnsHere(CharacterSpawnPoint point)
        {
            bool client = ClientMode;
            if (FreezeRules.SpawnsOwn(
                client, client && LocalOnly(point.gameObject)))
            {
                return true;
            }

            Plugin.Log.LogInfo(
                $"[enemy] {point.type} at "
                    + $"{point.transform.position}: not spawned here, the "
                    + "host's comes from the host");
            return false;
        }

        /// <summary>
        /// Character.heardSound runs: anything but a frozen one in the
        /// host's world.
        /// </summary>
        public bool Hears(Character c)
        {
            bool client = ClientMode;
            return FreezeRules.Hears(client, client && _frozen.Contains(c));
        }

        /// <summary>
        /// Location.activateCharacters(active) has run: the copies of the
        /// host's dead enemies made from their prefab go off and on with it.
        /// </summary>
        public void LocationSwitched(Location loc, bool active)
        {
            if (ClientMode)
            {
                LocationCharacters(loc, active);
            }
        }

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
