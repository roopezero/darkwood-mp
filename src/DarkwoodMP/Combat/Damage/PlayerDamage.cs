using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// PlayerDamage, host -> client: an enemy on the host hit this client's
    /// proxy (the core's PlayerDamageMessage has the bytes, DamageRules
    /// what becomes of the hit). The client applies it to its own Player as
    /// the game would have, effects (bleeding, poison...) included.
    /// Send and the effects' bytes are static and keep no state: the test
    /// dummy is hit with no session too, and NetEnemies and HostileNpcs
    /// write and read effects with their own messages.
    /// </summary>
    internal sealed class PlayerDamage
    {
        private readonly CoopSession _coop;

        public PlayerDamage(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.PlayerDamage, OnDamage);
        }

        public static void Send(
            ulong owner, float damage, Transform attacker, bool shadow,
            List<InvItemEffect> effects)
        {
            string from = "?";
            if (attacker != null)
            {
                from = attacker.name;
            }

            var session = Session.Instance;
            switch (DamageRules.Send(FactsOf(owner, attacker, session)))
            {
                case ProxyHit.LeftToTheirCopy:
                    // A friendly NPC is each player's own: one the host
                    // doesn't run as an enemy (Combat.HostileNpcs) hits the
                    // joiner with the joiner's own copy, so the host's
                    // copy's hit on the proxy would be a second one.
                    if (Threat.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[threat] {from} (an NPC) hit a remote player's "
                                + "stand-in: left to that player's own copy "
                                + "of it");
                    }

                    return;
                case ProxyHit.ToDummy:
                    HitDummy(damage, from, effects);
                    return;
                case ProxyHit.NotSent:
                    return;
            }

            var w = session.Begin(MsgType.PlayerDamage);
            var hit = new StandInHit
            {
                Damage = damage,
                Shadow = shadow,
                Attacker = from,
                Effects = SentOf(effects),
            };

            PlayerDamageMessage.Write(w, hit);
            session.SendTo(owner, w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// The attacker, the stand-in's owner and the session: the only code
        /// reading them for DamageRules.Send.
        /// </summary>
        private static ProxyHitFacts FactsOf(
            ulong owner, Transform attacker, Session session)
        {
            Character npc = null;
            if (attacker != null)
            {
                npc = attacker.GetComponentInParent<Character>();
            }

            bool byNpc = npc != null && npc.npc != null;
            return new ProxyHitFacts
            {
                ByNpc = byNpc,
                NpcIsEnemy = byNpc && NetEnemies.Eligible(npc),
                ToDummy = owner == PlayerSync.DummyId,
                Host = session != null && session.IsHost,
            };
        }

        private static void HitDummy(
            float damage, string from, List<InvItemEffect> effects)
        {
            string effectNote = "";
            if (effects != null && effects.Count > 0)
            {
                effectNote = $" (+{effects.Count} effects)";
            }

            Plugin.Log.LogInfo(
                $"[dummy] hit for {damage:0} by {from}" + effectNote);
            PlayerSync instance = Plugin.Instance.Host.Players;
            if (instance != null)
            {
                instance.FlashDummy(damage);
            }
        }

        /// <summary>
        /// u8 n, n x effect (the core's SentEffects).
        /// </summary>
        public static void WriteEffects(
            PacketWriter w, List<InvItemEffect> effects)
        {
            SentEffects.Write(w, SentOf(effects));
        }

        public static List<InvItemEffect> ReadEffects(PacketReader r) =>
            GameOf(SentEffects.Read(r));

        internal static List<SentEffect> SentOf(List<InvItemEffect> effects)
        {
            if (effects == null)
            {
                return null;
            }

            var sent = new List<SentEffect>(effects.Count);
            foreach (var e in effects)
            {
                sent.Add(new SentEffect
                {
                    Type = (int)e.type,
                    StartDelay = e.startDelay,
                    Duration = e.duration,
                    Modifier = e.modifier,
                    Interval = e.interval,
                    StopsBleeding = e.stopsBleeding,
                    StopsPoison = e.stopsPoison,
                    PoisonOverlay = e.hasPoisonOverlay,
                    Sound = e.activateSound,
                });
            }

            return sent;
        }

        internal static List<InvItemEffect> GameOf(
            IReadOnlyList<SentEffect> sent)
        {
            var effects = new List<InvItemEffect>();
            if (sent == null)
            {
                return effects;
            }

            foreach (var e in sent)
            {
                effects.Add(new InvItemEffect
                {
                    type = (CharacterEffectType)e.Type,
                    startDelay = e.StartDelay,
                    duration = e.Duration,
                    modifier = e.Modifier,
                    interval = e.Interval,
                    stopsBleeding = e.StopsBleeding,
                    stopsPoison = e.StopsPoison,
                    hasPoisonOverlay = e.PoisonOverlay,
                    activateSound = e.Sound,
                });
            }

            return effects;
        }

        private void OnDamage(ulong from, PacketReader r)
        {
            var session = _coop.Net;
            if (!DamageRules.Reads(session.IsClient, from == session.HostId))
            {
                return;
            }

            var hit = PlayerDamageMessage.Read(r);
            var player = Player.Instance;
            if (!DamageRules.Takes(player != null, player != null
                && player.alive))
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"Hit for {hit.Damage:0} by {hit.Attacker} on the host");
            if (hit.Shadow)
            {
                player.getHitByShadow(hit.Damage);
            }
            else
            {
                player.getHit(hit.Damage);
            }

            foreach (var e in GameOf(hit.Effects))
            {
                player.effects.activate(e);
            }
        }
    }
}
