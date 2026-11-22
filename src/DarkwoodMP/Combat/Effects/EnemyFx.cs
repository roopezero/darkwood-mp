using System.Collections.Generic;
using DarkwoodMP.Combat.Effects;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// What the host's enemies do that a joiner sees or hears on its own
    /// screen, beyond their sounds and blood (NetEnemies) and explosions
    /// (EnemyAreaHits):
    ///
    /// - Combat music (CombatMusic). The game plays it while
    ///   `Player.charactersAttackingMe` has someone in it, which
    ///   `Character.addToPlayerAttackers` fills only for the host's player. The
    ///   host checks twice a second, for each joiner's proxy, whether one of
    ///   its enemies is chasing that proxy (behaviour chasingTarget with the
    ///   proxy as target, as `addToPlayerAttackers` counts it for the host's
    ///   player), and tells that joiner when it changes.
    /// - Effect prefabs (EnemyEffect): the banshee's scream
    ///   (`Character.bansheeScream` spawns FX/BansheeScream, whose collider
    ///   shakes the player's camera and switches off lights it reaches), the
    ///   forest spirit's teleport and spawn effect (ForestSpirit_fastSpawnEff
    ///   from the FX pool), and an enemy's particle emitter switched by its
    ///   animation (`Character.modifyEmitter`), which frozen copies on a joiner
    ///   never run. A joiner makes the same prefab in its game, so the scream
    ///   there shakes its own camera and switches off its own copies of the
    ///   lights.
    /// - The banshee's sight (no message): what a player sees is their own
    ///   screen, so a joiner checks itself, twice a second, whether its player
    ///   sees one of its copies of the host's banshees (`Player.isInSight`, as
    ///   the banshee's `checkIfInSightOfPlayer` does), and shows the overlay,
    ///   the agitated sound, the shake and the banshee's sight light as
    ///   `onBansheeSeePlayer` / `bansheeAgitated` do. Where the banshee goes is
    ///   the host's.
    /// </summary>
    internal sealed partial class EnemyFx
    {
        private readonly CoopSession _coop;
        private Seconds _next;

        public EnemyFx(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.CombatMusic, OnCombatMusic);
            coop.Handle(MsgType.EnemyEffect, OnEffect);
            coop.Net.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => coop.Net.OnPeerDisconnected -= OnPeerLeft);
        }

        private Session Net => _coop.Net;

        private bool Hosting => EffectRules.Hosting(
            Net.IsHost, Net.Peers.Count, WorldShare.InWorld);

        private void OnPeerLeft(ulong id) => _inCombat.Remove(id);

        public void Update()
        {
            var tick = EffectRules.Due(new Seconds(Time.unscaledTime), _next);
            if (!tick.Due)
            {
                return;
            }

            _next = tick.Next;
            if (Hosting)
            {
                HostCombat();
            }

            if (EffectRules.Client(NetEnemies.ClientMode, WorldShare.InWorld))
            {
                BansheeSight();
            }
        }

        // ---- combat music ---------------------------------------------------

        private readonly Dictionary<ulong, bool> _inCombat =
            new Dictionary<ulong, bool>();

        /// <summary>
        /// Client: the host says one of its enemies is after our player.
        /// </summary>
        public bool HostSaysInCombat { get; private set; }

        private void HostCombat()
        {
            var s = Net;
            var chars = Plugin.Instance.Host.Characters.Active();
            foreach (var proxy in NetPlayerProxy.All.Values)
            {
                if (proxy == null)
                {
                    continue;
                }

                bool fight = proxy.alive && Chased(proxy, chars);
                _inCombat.TryGetValue(proxy.Owner, out bool was);
                var change = EffectRules.Change(
                    fight, was, proxy.Owner == Sync.Players.PlayerSync.DummyId);
                if (change == CombatChange.Same)
                {
                    continue;
                }

                _inCombat[proxy.Owner] = fight;
                if (Plugin.Instance.Host.Threats.Debug || WorldActions.Debug)
                {
                    string text = "is no longer";
                    if (fight)
                    {
                        text = "is";
                    }

                    Plugin.Log.LogInfo(
                        $"[enemyfx] {proxy.OwnerName} {text} in combat");
                }

                if (change == CombatChange.Kept)
                {
                    continue;
                }

                var w = s.Begin(MsgType.CombatMusic);
                CombatMusicMessage.Write(w, fight);
                s.SendTo(proxy.Owner, w, Channel.Control, reliable: true);
            }
        }

        private static bool Chased(
            NetPlayerProxy proxy, IEnumerable<Character> chars)
        {
            foreach (var c in chars)
            {
                if (EffectRules.Chases(FactsOf(c, proxy)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One of the host's characters and a joiner's stand-in: the only
        /// code reading them for EffectRules.Chases.
        /// </summary>
        private static ChaserFacts FactsOf(Character c, NetPlayerProxy proxy)
        {
            if (c == null || !c.alive)
            {
                return new ChaserFacts();
            }

            return new ChaserFacts
            {
                Alive = true,
                Enemy = NetEnemies.Eligible(c),
                Chasing = c.behaviour == Character.Behaviour.chasingTarget,
                OnStandIn = c.target == proxy.transform,
            };
        }

        private void OnCombatMusic(ulong from, PacketReader r)
        {
            var s = Net;
            if (!s.IsClient || from != s.HostId)
            {
                return;
            }

            HostSaysInCombat = CombatMusicMessage.Read(r);
            var me = Player.Instance;
            if (me == null || !WorldShare.InWorld)
            {
                return;
            }

            int attackers = 0;
            if (me.charactersAttackingMe != null)
            {
                attackers = me.charactersAttackingMe.Count;
            }

            bool want = EffectRules.Music(HostSaysInCombat, attackers);
            if (want != me.InCombat)
            {
                // the setter starts or fades out the combat track
                me.InCombat = want;
            }

            string text = "off";
            if (want)
            {
                text = "on";
            }

            string hostSays = "none is";
            if (HostSaysInCombat)
            {
                hostSays = "an enemy is after us";
            }

            Plugin.Log.LogInfo(
                $"[enemyfx] combat music {text} (the host says {hostSays})");
        }

        /// <summary>
        /// Client, Player.checkInCombatChars: whether the game's own count
        /// of attackers (in an area of our own) is skipped, so it doesn't
        /// turn off the host's combat music.
        /// </summary>
        internal bool KeepsMusic(Player me)
        {
            if (!EffectRules.KeepsMusic(
                    HostSaysInCombat, NetEnemies.ClientMode))
            {
                return false;
            }

            if (!me.InCombat)
            {
                // (setting it off and on again would fade the music out)
                me.InCombat = true;
            }

            return true;
        }

        // ---- effect prefabs -------------------------------------------------

        /// <summary>
        /// Host: one of its enemies made a prefab (kind 1) or a pooled FX
        /// effect (kind 2) here.
        /// </summary>
        internal void SendPrefab(
            byte kind, string name, Vector3 pos, float rotY)
        {
            if (!EffectRules.Sends(Hosting, Coop.Guards.Replaying))
            {
                return;
            }

            var s = Net;
            var w = s.Begin(MsgType.EnemyEffect);
            var fx = new EnemyEffect
            {
                Kind = kind,
                Name = name,
                At = new Vec3(pos.x, pos.y, pos.z),
                RotY = rotY,
            };

            EnemyEffectMessage.Write(w, fx);
            s.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[enemyfx] sent {name} at {pos.x:0},{pos.z:0}");
            }
        }

        /// <summary>
        /// Host, after Character.bansheeScream: FX/BansheeScream where it
        /// stands goes to the joiners too.
        /// </summary>
        internal void Screamed(Character c)
        {
            if (NetEnemies.Eligible(c))
            {
                SendPrefab(
                    EffectRules.PrefabKind, EffectRules.BansheeScream,
                    c.transform.position, 0f);
            }
        }

        /// <summary>
        /// Host, after Core.AddPooledPrefab: the forest spirit's teleport
        /// and spawn go to the joiners too. Not when the pool hasn't got it
        /// here (the call throws, so there is no result).
        /// </summary>
        internal void Pooled(
            string pool, string prefab, Vector3 at, Transform made)
        {
            if (EffectRules.SpreadsPooled(made != null, pool, prefab))
            {
                SendPrefab(EffectRules.PooledKind, prefab, at, 0f);
            }
        }

        /// <summary>
        /// Host, after Character.modifyEmitter: an enemy's animation switched
        /// its particle emitter; the players that have it see it too.
        /// </summary>
        internal void EmitterSwitched(Character c, int toggle)
        {
            if (!EffectRules.SpreadsEmitter(
                    NetEnemies.Eligible(c), c._particleLight != null,
                    c._particleEmitter != null))
            {
                return;
            }

            if (!EffectRules.Sends(Hosting, Coop.Guards.Replaying))
            {
                return;
            }

            NetEnemies e = _coop.Enemies;
            if (e == null || !e.IdOf(c, out uint id))
            {
                return;
            }

            var s = Net;
            var fx = new EnemyEffect
            {
                Kind = EffectRules.EmitterKind,
                Id = id,
                Toggle = toggle,
            };

            foreach (var peer in e.PeersSeeing(c))
            {
                var w = s.Begin(MsgType.EnemyEffect);
                EnemyEffectMessage.Write(w, fx);
                s.SendTo(peer, w, Channel.Control, reliable: true);
            }
        }

        private void OnEffect(ulong from, PacketReader r)
        {
            var s = Net;
            if (!s.IsClient || from != s.HostId
                || !WorldShare.InWorld)
            {
                return;
            }

            var fx = EnemyEffectMessage.Read(r);
            var pos = new Vector3(fx.At.X, fx.At.Y, fx.At.Z);
            var me = Player.Instance;
            bool away = EnemyAreaHits.PlayerAway();
            float d = 0f;
            if (me != null)
            {
                d = Core.trueDistance(me.transform.position, pos);
            }

            var show = EffectRules.Show(
                fx.Kind, EffectRules.Shown(me != null, away, d));
            if (show == EffectShow.Emitter)
            {
                ShowEmitter(fx);
                return;
            }

            if (show == EffectShow.NotShown)
            {
                var log = Plugin.Log;
                string where = "away";
                if (me != null && !away)
                {
                    where =
                        "too far (at "
                            + $"{me.transform.position.x:0},"
                            + $"{me.transform.position.z:0})";
                }

                log.LogInfo(
                    $"[enemyfx] the host's enemy made {fx.Name}, not shown: "
                        + $"our player is {where}");
                return;
            }

            if (Show(() =>
                {
                    if (show == EffectShow.Prefab)
                    {
                        Core.AddPrefab(
                            fx.Name, pos, Quaternion.Euler(90f, fx.RotY, 0f),
                            null);
                    }
                    else
                    {
                        Core.AddPooledPrefab(
                            EffectRules.Pool, fx.Name, pos,
                            Quaternion.identity);
                    }
                }))
            {
                Plugin.Log.LogInfo(
                    $"[enemyfx] the host's enemy made {fx.Name} at "
                        + $"{pos.x:0},{pos.z:0}");
            }
        }

        private static void ShowEmitter(in EnemyEffect fx)
        {
            NetEnemies instance = null;
            if (Coop.Current != null)
            {
                instance = Coop.Current.Enemies;
            }

            Character c = null;
            if (instance != null)
            {
                c = instance.PuppetOf(fx.Id);
            }

            if (c == null)
            {
                return;
            }

            int toggle = fx.Toggle;
            Show(() => c.modifyEmitter(toggle));
            string text = "on";
            if (toggle == 0)
            {
                text = "off";
            }

            Plugin.Log.LogInfo($"[enemyfx] {c.name}'s emitter {text}");
        }

        /// <summary>
        /// Makes an effect the host's enemy made, as a replay (nothing in it is
        /// sent back).
        /// </summary>
        private static bool Show(System.Action make)
        {
            Coop.Guards.Replaying = true;
            try
            {
                make();
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogInfo(
                    "[enemyfx] couldn't show the host's effect here: "
                        + $"{e.Message}");
                return false;
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }
    }
}
