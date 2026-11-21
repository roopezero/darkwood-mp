using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Hostiles;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;
using Object = UnityEngine.Object;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The NPCs that turned on a player in this session (HostileNpcs says
    /// how), the client's hits and scripted attacks left to the host, and the
    /// host's answers to them. One per session (CoopSession.Hostiles).
    /// </summary>
    internal sealed class NpcHostility
    {
        private readonly CoopSession _coop;

        // Host: the NPCs it runs as enemies. Client: its copies of those.
        private readonly HashSet<Character> _running =
            new HashSet<Character>();

        // NPCs that died in this game while playing together. A corpse loses
        // its NPC part (Character.destroyComponents), so from then on it looks
        // like any creature's: one the host runs and sends, and that a client
        // freezes and hides until the host's comes (NetEnemies.ClientSweep).
        private readonly HashSet<Character> _dead = new HashSet<Character>();

        /// <summary>
        /// Set while a client applies its own hit or attack to an NPC the host
        /// hasn't got.
        /// </summary>
        private bool _ours;

        // Client: the NPC whose hit was just left to the host, in this frame.
        // MeleeSensor and Flame activate the weapon's effects right after
        // getHit; those went to the host with the hit, so they don't run here
        // too.
        private Character _forwarded;
        private int _forwardedFrame = -1;

        public NpcHostility(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.NpcHit, OnNpcHit);
            coop.Handle(MsgType.NpcAttack, OnNpcAttack);
        }

        /// <summary>Whether the running set holds this NPC.</summary>
        public bool Listed(Character c) => _running.Contains(c);

        /// <summary>
        /// Client: whether effects activating on this NPC now are those of a
        /// hit just left to the host.
        /// </summary>
        public bool EffectsForwarded(Character c)
        {
            return HostileRules.EffectsForwarded(new ForwardFacts
            {
                Ours = _ours,
                Forwarded = c != null && c == _forwarded,
                Frame = Time.frameCount,
                ForwardedFrame = _forwardedFrame,
            });
        }

        private bool Forwarded(Character c)
        {
            _forwarded = c;
            _forwardedFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// Client: the melee hit is over (MeleeSensorNotesEffects).
        /// </summary>
        public void EndHit() => _forwarded = null;

        /// <summary>
        /// An NPC died in this game (NpcDeaths.AfterDie): its corpse is
        /// still our copy of that NPC.
        /// </summary>
        public void Died(Character c)
        {
            if (c != null)
            {
                _dead.Add(c);
            }
        }

        public void HostStarted(Character c, uint id)
        {
            _running.Add(c);
            Plugin.Log.LogInfo(
                $"[npc] {c.name} turned hostile ({c.aggressiveness}, "
                    + $"{c.behaviour}, after {HostileNpcs.Who(c.target)}): "
                    + $"the host runs it as enemy #{id}");
        }

        public void HostStopped(Character c, uint id)
        {
            _running.Remove(c);
            Plugin.Log.LogInfo(
                $"[npc] {c.name} (#{id}) calmed down: each player's own again");
        }

        public void ClientStarted(Character c, uint id)
        {
            _running.Add(c);
            Plugin.Log.LogInfo(
                $"[npc] {c.name} is hostile in the host's game: shown as its "
                    + $"enemy #{id}");
        }

        /// <summary>
        /// Client: the host stopped sending this NPC. Ours from the save runs
        /// again here; true if it was handled.
        /// </summary>
        public bool ClientStopped(Character c, uint id, bool ours)
        {
            _running.Remove(c);
            if (ours)
            {
                return false; // made from its prefab: it goes
            }

            NetEnemies.Thaw(c);
            Plugin.Log.LogInfo(
                $"[npc] the host stopped sending {c.name} (#{id}): it's ours "
                    + "to run again");
            return true;
        }

        /// <summary>
        /// Client: our copy of the host's NPC of this kind when its save id
        /// didn't name it (one a scripted event made in each game, or a
        /// location each game loaded for itself while playing together): the
        /// nearest one of ours within 3000 that isn't driven yet. Our corpse of
        /// an NPC that died here counts too: the host sends its corpse of a
        /// killed NPC as one of its creatures (the NPC part is gone), and
        /// without ours to show it as, our corpse stayed frozen and hidden
        /// while nothing stood in for the host's (the chicken lady's corpse
        /// vanished for joiners and couldn't be looted).
        /// </summary>
        public Character OurCopy(
            string kind, Vector3 pos, Func<Character, bool> driven,
            Func<Character, string> kindOf)
        {
            var chars = new List<Character>();
            var ours = new List<HostileCandidate>();
            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c == null)
                {
                    continue;
                }

                chars.Add(c);
                ours.Add(CandidateOf(c, driven, kindOf));
            }

            int at = HostileRules.OurCopy(ours, kind, VecOf(pos));
            if (at < 0)
            {
                return null;
            }

            var best = chars[at];
            Plugin.Log.LogInfo(
                $"[npc] the host's {kind} is our {best.name} near it");
            return best;
        }

        /// <summary>
        /// One of our characters as HostileRules.OurCopy reads it. Whether it
        /// is driven and its kind are asked only of an NPC or one that died
        /// here, as the game's order did.
        /// </summary>
        private HostileCandidate CandidateOf(
            Character c, Func<Character, bool> driven,
            Func<Character, string> kindOf)
        {
            bool npc = c.npc != null;
            bool died = _dead.Contains(c);
            if (!npc && !died)
            {
                return new HostileCandidate();
            }

            bool isDriven = driven(c);
            string kind = null;
            if (!isDriven)
            {
                kind = kindOf(c);
            }

            return new HostileCandidate
            {
                Pos = VecOf(c.transform.position),
                IsNpc = npc,
                DiedHere = died,
                Driven = isDriven,
                Kind = kind,
            };
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        // ---- client: our player's hit or attack goes to the host ------------

        /// <summary>
        /// Client, from CharacterGetHitFromClient: a hit on an NPC of ours
        /// that the host doesn't run yet. True if it's handled (sent, or left
        /// to the host's copy): don't apply it here.
        /// </summary>
        public bool ClientHit(in HostileNpcs.Hit hit)
        {
            var c = hit.Npc;
            var effects = NetEnemies.HitEffects;
            var way = NpcHitRules.Way(FactsOf(hit, effects));
            if (way == NpcHitWay.Ours)
            {
                Plugin.Log.LogInfo(
                    $"[npc] our hit on {c.name} is ours (alive {c.alive}, "
                        + $"night trader {c.isNightTrader}, local "
                        + $"{NetEnemies.LocalOnly(c)})");
                return false;
            }

            if (way == NpcHitWay.Thrown)
            {
                return Forwarded(c);
            }

            if (way != NpcHitWay.Sent)
            {
                return false;
            }

            Send(hit, effects);
            Core.AddPrefab(
                "FX/Bloodsplats/Shotsplat_stay", c.transform.position,
                Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f),
                null);
            return Forwarded(c);
        }

        private NpcHitFacts FactsOf(
            in HostileNpcs.Hit hit, List<InvItemEffect> effects)
        {
            var c = hit.Npc;
            if (c == null || c.npc == null)
            {
                return new NpcHitFacts { Ours = _ours };
            }

            var flame = ThrownHits.CurrentFlame;
            var attacker = hit.Attacker;
            int count = 0;
            if (effects != null)
            {
                count = effects.Count;
            }

            return new NpcHitFacts
            {
                Ours = _ours,
                IsNpc = true,
                Alive = c.alive,
                NightTrader = c.isNightTrader,
                LocalOnly = NetEnemies.LocalOnly(c),
                Thrown = ThrownHits.Now(),
                Flame = flame != null,
                FlameOurs = flame != null && EnemyAreaHits.ByLocalPlayer(flame),
                ByPlayer = hit.ByPlayer,
                AttackerIsPlayer = attacker != null
                    && attacker.GetComponent<Player>() != null,
                Damage = hit.Damage,
                Effects = count,
            };
        }

        private void Send(in HostileNpcs.Hit hit, List<InvItemEffect> effects)
        {
            var c = hit.Npc;
            float damage = hit.Damage;
            bool cut = hit.Cut;
            bool interrupt = hit.Interrupt;
            bool normal = hit.Normal;
            var w = _coop.Net.Begin(MsgType.NpcHit);
            NpcHitMessage.Write(w, new NpcHitRequest
            {
                Npc = ObjRef.NameOf(ObjRef.Of(c.gameObject)),
                Damage = damage,
                Flags = NpcHitMessage.FlagsOf(cut, interrupt, normal),
                Effects = PlayerDamage.SentOf(effects),
                Weapon = WeaponJson(),
            });

            List<InvItemEffect> mine = null;
            if (effects != null)
            {
                mine = new List<InvItemEffect>(effects);
            }

            Plugin.Log.LogInfo(
                $"[npc] our hit on {c.name} for {damage:0} goes to the host");
            _coop.Authority.Request(w, $"hit on {c.name} for {damage:0}",
                (ok, reason, result) =>
            {
                var answer = NpcHitRules.AfterHit(new NpcAnswerFacts
                {
                    Accepted = ok,
                    Reason = reason,
                    Alive = c != null && c.alive,
                    HasPlayer = Player.Instance != null,
                });

                if (answer == NpcHitAnswer.HostApplied)
                {
                    ScriptedEvents.HitApplied(c, result);
                    return;
                }

                if (answer != NpcHitAnswer.OursHere)
                {
                    return;
                }

                // Only our game has it: our hit, here.
                _ours = true;
                try
                {
                    c.getHit(
                        damage, Player.Instance.transform, cut, true, interrupt,
                        normal, false, false, false);
                    if (mine != null && c.effects != null)
                    {
                        foreach (var e in mine)
                        {
                            c.effects.activate(e);
                        }
                    }
                }
                finally
                {
                    _ours = false;
                }
            }, inventory: false);
        }

        /// <summary>Our player's item as its save state's JSON; "" for none.
        /// </summary>
        private static string WeaponJson()
        {
            InvItemClass item = null;
            if (Player.Instance != null)
            {
                item = Player.Instance.currentItem;
            }

            if (InvItemClass.isNull(item))
            {
                return "";
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(
                new InvItemClass.SaveState(item, 0));
        }

        /// <summary>
        /// Client: a scripted "attack the player" on an NPC of ours. True if it
        /// isn't to be done here.
        /// </summary>
        public bool ClientAttack(Character c)
        {
            var way = NpcHitRules.AttackWay(AttackOf(c));
            if (way != NpcAttackWay.Send)
            {
                return way == NpcAttackWay.Skip;
            }

            var w = _coop.Net.Begin(MsgType.NpcAttack);
            NpcAttackMessage.Write(w, ObjRef.NameOf(ObjRef.Of(c.gameObject)));
            _coop.Authority.Request(
                w, $"{c.name} attacks us", (ok, reason, result) =>
            {
                bool alive = c != null && c.alive;
                if (!NpcHitRules.OursToAttack(ok, reason, alive))
                {
                    return;
                }

                _ours = true;
                try
                {
                    c.attackPlayer();
                }
                finally
                {
                    _ours = false;
                }
            }, inventory: false);

            return true;
        }

        private NpcAttackFacts AttackOf(Character c)
        {
            if (c == null || c.npc == null)
            {
                return new NpcAttackFacts { Ours = _ours };
            }

            bool copy = false;
            var ne = NetEnemies.Instance;
            if (ne != null)
            {
                copy = ne.IsPuppet(c, out _);
            }

            return new NpcAttackFacts
            {
                Ours = _ours,
                IsNpc = true,
                Dummy = c.dummy,
                ClientMode = NetEnemies.ClientMode,
                LocalOnly = NetEnemies.LocalOnly(c),
                Copy = copy,
                Frozen = NetEnemies.Frozen.Contains(c),
                Replaying = _coop.Guards.Replaying,
                SharedOnly =
                    ScriptedEvents.Current == EventPart.SharedOnly,
            };
        }

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// Host: the NPC a client's request names, as the host's world has it.
        /// </summary>
        private static NpcAskFacts AskOf(
            ulong from, Character c, out NetPlayerProxy proxy)
        {
            proxy = null;
            if (c == null || c.npc == null)
            {
                return new NpcAskFacts { Found = c != null };
            }

            bool inPlay = NetEnemies.InPlay(c);
            bool standIn = false;
            if (inPlay)
            {
                standIn = NetPlayerProxy.All.TryGetValue(from, out proxy)
                    && proxy != null;
            }

            return new NpcAskFacts
            {
                Found = true,
                IsNpc = true,
                InPlay = inPlay,
                HasStandIn = standIn,
            };
        }

        private void OnNpcHit(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (!s.IsHost)
            {
                return;
            }

            var hit = NpcHitMessage.Read(r);
            var c = ObjRef.Resolve<Character>(ObjRef.RefOf(hit.Npc));
            var verdict = HostileRules.Resolve(AskOf(from, c, out var proxy));
            if (verdict.Refused)
            {
                _coop.Authority.Refuse(verdict);
                return;
            }

            float damage = hit.Damage;
            Plugin.Log.LogInfo(
                $"[npc] {s.NameOf(from)} hit {c.name} for {damage:0}");
            NetEnemies instance = NetEnemies.Instance;
            if (instance != null)
            {
                var effects = PlayerDamage.GameOf(hit.Effects);
                instance.ApplyHit(
                    from, proxy, c, damage, hit.Flags, effects, hit.Weapon);
            }
        }

        private void OnNpcAttack(ulong from, PacketReader r)
        {
            if (!_coop.Net.IsHost)
            {
                return;
            }

            var npc = NpcAttackMessage.Read(r);
            var c = ObjRef.Resolve<Character>(ObjRef.RefOf(npc));
            var ask = AskOf(from, c, out var proxy);
            var verdict = HostileRules.Resolve(ask);
            if (!verdict.Refused)
            {
                var reach = _coop.Authority.ReachOf(c.transform.position) with
                {
                    Reach = HostileRules.AttackReach,
                    What = c.name,
                };

                verdict = HostileRules.MayAttack(ask, reach);
            }

            if (verdict.Refused)
            {
                _coop.Authority.Refuse(verdict);
                return;
            }

            HostileNpcs.SendAfter(c, proxy);
        }
    }
}
