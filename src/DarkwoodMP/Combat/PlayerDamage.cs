using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// PlayerDamage, host -> client: an enemy on the host hit this client's
    /// proxy.
    ///   f32 damage, u8 flags (1: shadow), str attacker, u8 n, n x effect
    ///   effect: i32 type, f32 startDelay, f32 duration, f32 modifier,
    ///           f32 interval, u8 flags (1 stopsBleeding, 2 stopsPoison,
    ///           4 hasPoisonOverlay), str activateSound
    /// The client applies it to its own Player as the game would have, effects
    /// (bleeding, poison...) included.
    /// </summary>
    public static class PlayerDamage
    {
        public static void Register(Session s) =>
            s.Register(MsgType.PlayerDamage, OnDamage);

        public static void Send(
            ulong owner, float damage, Transform attacker, bool shadow,
            List<InvItemEffect> effects)
        {
            string from = "?";
            if (attacker != null)
            {
                from = attacker.name;
            }
            // A friendly NPC is each player's own: one the host doesn't run as
            // an enemy (Combat.HostileNpcs) hits the joiner with the joiner's
            // own copy, so the host's copy's hit on the proxy would be a second
            // one.
            Character npc = null;
            if (attacker != null)
            {
                npc = attacker.GetComponentInParent<Character>();
            }
            if (npc != null && npc.npc != null && !NetEnemies.Eligible(npc))
            {
                if (Threat.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[threat] {from} (an NPC) hit a remote player's stand-in: left to that player's own copy of it");
                }
                return;
            }
            if (owner == PlayerSync.DummyId)
            {
                string effectNote = "";
                if (effects != null && effects.Count > 0)
                {
                    effectNote = $" (+{effects.Count} effects)";
                }
                Plugin.Log.LogInfo(
                    $"[dummy] hit for {damage:0} by {from}" + effectNote);
                PlayerSync instance = PlayerSync.Instance;
                if (instance != null)
                {
                    instance.FlashDummy(damage);
                }
                return;
            }
            var session = Session.Instance;
            if (session == null || !session.IsHost)
            {
                return;
            }
            var w = session.Begin(MsgType.PlayerDamage);
            w.W.Write(damage);
            int shadowFlag = 0;
            if (shadow)
            {
                shadowFlag = 1;
            }
            w.W.Write((byte)shadowFlag);
            w.WriteStr(from);
            WriteEffects(w, effects);
            session.SendTo(owner, w, Channel.Control, reliable: true);
        }

        /// <summary>u8 n, n x effect (see the class's summary).</summary>
        public static void WriteEffects(
            PacketWriter w, List<InvItemEffect> effects)
        {
            int n;
            if (effects == null)
            {
                n = 0;
            }
            else
            {
                n = Mathf.Min(effects.Count, 16);
            }
            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var e = effects[i];
                w.W.Write((int)e.type);
                w.W.Write(e.startDelay);
                w.W.Write(e.duration);
                w.W.Write(e.modifier);
                w.W.Write(e.interval);
                int flags = 0;
                if (e.stopsBleeding)
                {
                    flags |= 1;
                }
                if (e.stopsPoison)
                {
                    flags |= 2;
                }
                if (e.hasPoisonOverlay)
                {
                    flags |= 4;
                }
                w.W.Write((byte)flags);
                string activateSound = e.activateSound;
                if (activateSound == null)
                {
                    activateSound = "";
                }
                w.WriteStr(activateSound);
            }
        }

        public static List<InvItemEffect> ReadEffects(PacketReader r)
        {
            int n = r.R.ReadByte();
            var effects = new List<InvItemEffect>();
            for (int i = 0; i < n; i++)
            {
                var e = new InvItemEffect
                {
                    type = (CharacterEffectType)r.R.ReadInt32(),
                    startDelay = r.R.ReadSingle(),
                    duration = r.R.ReadSingle(),
                    modifier = r.R.ReadSingle(),
                    interval = r.R.ReadSingle(),
                };
                byte f = r.R.ReadByte();
                e.stopsBleeding = (f & 1) != 0;
                e.stopsPoison = (f & 2) != 0;
                e.hasPoisonOverlay = (f & 4) != 0;
                string snd = r.ReadStr();
                if (snd.Length > 0)
                {
                    e.activateSound = snd;
                }
                else
                {
                    e.activateSound = null;
                }
                effects.Add(e);
            }
            return effects;
        }

        private static void OnDamage(ulong from, PacketReader r)
        {
            var session = Session.Instance;
            if (session == null || !session.IsClient || from != session.HostId)
            {
                return;
            }
            float damage = r.R.ReadSingle();
            bool shadow = (r.R.ReadByte() & 1) != 0;
            string attacker = r.ReadStr();
            var effects = ReadEffects(r);

            var player = Player.Instance;
            if (player == null || !player.alive)
            {
                return;
            }
            Plugin.Log.LogInfo($"Hit for {damage:0} by {attacker} on the host");
            if (shadow)
            {
                player.getHitByShadow(damage);
            }
            else
            {
                player.getHit(damage);
            }
            foreach (var e in effects)
            {
                player.effects.activate(e);
            }
        }
    }
}
