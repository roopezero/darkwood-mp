using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// What the local player sounds and looks like to the others, beyond the
    /// sprites PlayerSync sends: the sounds the game plays on our player
    /// (steps, shots, reloads, hammering, pain), a gun's muzzle flash, the
    /// blood when we're hurt and the flames while we burn. Each player's game
    /// sends its own; the host passes a client's on to the others (except the
    /// sender), and every game shows it on that player's puppet. It's
    /// presentation only: nothing in it changes the world, so the host has
    /// nothing to check.
    /// </summary>
    internal sealed class PlayerFx
    {
        /// <summary>
        /// How many of the other players' shots this game has shown on their
        /// puppets in this session (for the map test).
        /// </summary>
        public int ShotsSeen { get; private set; }

        private readonly Session _s;
        // our own: these are sent from inside game calls
        private readonly PacketWriter _w = new PacketWriter(new BepInLog());
        private readonly List<PlayedSound> _sounds = new List<PlayedSound>();
        private readonly List<PlayedSound> _relayed = new List<PlayedSound>();
        private Seconds _nextSounds;
        private bool _burningSent;
        private readonly HashSet<string> _logged = new HashSet<string>();

        public PlayerFx(CoopSession coop)
        {
            _s = coop.Net;
            coop.Handle(MsgType.PlayerSounds, OnSounds);
            coop.Handle(MsgType.PlayerShot, OnShot);
            coop.Handle(MsgType.PlayerHurt, OnHurt);
            // a newcomer hears whether we burn
            _s.OnPeerConnected += OnPeer;
            coop.Hook(() => _s.OnPeerConnected -= OnPeer);
        }

        private void OnPeer(ulong id)
        {
            _burningSent = false;
        }

        /// <summary>We're in a world with someone to tell.</summary>
        public bool Sharing => PlayerFxRules.Sharing(new SharingFacts
        {
            Active = _s.Active,
            Connected = _s.Connected,
            Client = _s.IsClient,
            Peers = _s.Peers.Count,
            InWorld = WorldShare.InWorld,
        });

        public void Update()
        {
            if (!Sharing)
            {
                _sounds.Clear();
                return;
            }

            var now = new Seconds(Time.unscaledTime);
            if (PlayerFxRules.SendsSounds(_sounds.Count, now, _nextSounds))
            {
                _nextSounds = PlayerFxRules.NextSounds(now);
                var w = Begin(MsgType.PlayerSounds);
                int n = PlayerFxRules.BatchSize(_sounds.Count);
                PlayerSounds.Write(w, _s.LocalId, _sounds.GetRange(0, n));
                Send(w, reliable: false);
                LogOnce(
                    "sent sounds",
                    $"[fx] sending our player's sounds ({_sounds[0].Id}...)");
                _sounds.Clear();
            }

            var p = Player.Instance;
            bool alive = p != null && p.alive;
            Burn burn = null;
            if (alive)
            {
                burn = p.GetComponent<Burn>();
            }

            bool burning = PlayerFxRules.Burning(alive, burn != null);
            if (PlayerFxRules.SendsBurning(burning, _burningSent))
            {
                _burningSent = burning;
                var hurt = new HurtLook { Kind = (byte)HurtKind.Stopped };
                if (burning)
                {
                    string prefab = null;
                    if (p.burnPrefab != null)
                    {
                        prefab = p.burnPrefab.name;
                    }

                    hurt = new HurtLook
                    {
                        Kind = (byte)HurtKind.Burning,
                        Prefab = PlayerFxRules.BurnFlames(
                            burn.noParticle, prefab),
                        Sound = PlayerFxRules.BurnSound(
                            burn.noSound, burn.burnSoundOverride),
                    };
                }

                var w = Begin(MsgType.PlayerHurt);
                PlayerHurt.Write(w, _s.LocalId, hurt);

                Send(w, reliable: true);
                string text = "stopped";
                if (burn != null)
                {
                    text = "started";
                }

                Plugin.Log.LogInfo($"[fx] sent: our player {text} burning");
            }
        }

        // ---- what our player does (called from PlayerFxPatches) ------------

        /// <summary>
        /// A sound the game played: if it's on our player (or by our player's
        /// own code), the others hear it too.
        /// </summary>
        public void Played(
            string audioId, float volume, Transform parent, AudioObject played)
        {
            if (played == null || !Sharing || !IsOurs(parent))
            {
                return;
            }

            // A loop (tiredness, low health, fire) is stopped by our game
            // later; a copy would never stop.
            var item = played.audioItem;
            bool loops = item != null
                && item.Loop != AudioItem.LoopMode.DoNotLoop;
            if (!PlayerFxRules.SendsSound(item != null, loops))
            {
                return;
            }

            OurSound(audioId, volume);
        }

        /// <summary>
        /// A player fired a gun: if it's ours, the others see the muzzle flash
        /// on our puppet.
        /// </summary>
        public void Fired(Player player)
        {
            bool ours = player == Player.Instance;
            InvItemClass currentItem = player.currentItem;
            InvItem gun = null;
            if (currentItem != null)
            {
                gun = currentItem.baseClass;
            }

            if (PlayerFxRules.ShotIsOurs(ours, Sharing, gun != null))
            {
                OurShot(gun);
            }
        }

        /// <summary>
        /// The game added a prefab: a blood splat of our player's getHit is
        /// shown on our puppet too.
        /// </summary>
        public void Added(string prefab, GameObject made)
        {
            var blood = new BloodFacts
            {
                InGetHit = _inGetHit,
                HasResult = made != null,
                Prefab = prefab,
                Sharing = Sharing,
                Replaying = Coop.Guards.Replaying,
            };

            if (PlayerFxRules.BloodIsOurs(blood))
            {
                OurBlood(prefab, made);
            }
        }


        private void OurSound(string audioId, float volume)
        {
            if (PlayerFxRules.Queues(_sounds.Count))
            {
                _sounds.Add(new PlayedSound { Id = audioId, Volume = volume });
            }
        }

        private void OurShot(InvItem gun)
        {
            string s = "";
            if (gun.muzzlePrefab != null)
            {
                s = gun.muzzlePrefab.name;
            }

            string particles = "";
            if (gun.muzzleParticles != null)
            {
                particles = gun.muzzleParticles.name;
            }

            byte flashFlag = 1;
            if (gun.noMuzzleFlash)
            {
                flashFlag = 0;
            }

            Vector3 offset = gun.muzzleOffset;
            var w = Begin(MsgType.PlayerShot);
            PlayerShot.Write(w, _s.LocalId, new MuzzleFlash
            {
                Muzzle = s,
                Particles = particles,
                Flash = flashFlag,
                Offset = new Vec3(offset.x, offset.y, offset.z),
            });

            Send(w, reliable: false);
            LogOnce("sent shot", "[fx] sending our player's shots");
        }

        private void OurBlood(string prefab, GameObject splat)
        {
            var p = Player.Instance;
            var w = Begin(MsgType.PlayerHurt);
            PlayerHurt.Write(w, _s.LocalId, new HurtLook
            {
                Kind = (byte)HurtKind.Blood,
                Prefab = prefab,
                Height = splat.transform.position.y - p.transform.position.y,
                Yaw = splat.transform.eulerAngles.y,
            });

            Send(w, reliable: false);
            Plugin.Log.LogInfo($"[fx] sent: our player bled ({prefab})");
        }

        /// <summary>
        /// A message about a player: its type, then (written by its message
        /// class) the player's id. A client's own id is replaced by the host
        /// anyway.
        /// </summary>
        private PacketWriter Begin(MsgType t)
        {
            _w.Begin(t);
            return _w;
        }

        private void Send(PacketWriter w, bool reliable)
        {
            if (_s.IsHost)
            {
                _s.Broadcast(w, Channel.State, reliable);
            }
            else
            {
                _s.SendToHost(w, Channel.State, reliable);
            }
        }

        // ---- what the others do ---------------------------------------------

        /// <summary>
        /// Who a message is about: on the host, always the client that sent it
        /// (whatever it says); on a client, what the host says, if it came from
        /// the host. 0 if it's not to be shown.
        /// </summary>
        private ulong Owner(ulong from, ulong said)
        {
            return PlayerFxRules.Owner(new OwnerFacts
            {
                Host = _s.IsHost,
                From = from,
                Said = said,
                HostId = _s.HostId,
                LocalId = _s.LocalId,
            });
        }

        /// <summary>
        /// The host passes a client's message on to the other clients, with the
        /// sender's id in it.
        /// </summary>
        private PacketWriter Relay(MsgType t)
        {
            if (_s.IsHost)
            {
                return Begin(t);
            }

            return null;
        }

        private void Relayed(PacketWriter w, ulong except, bool reliable)
        {
            if (w != null)
            {
                _s.Broadcast(w, Channel.State, reliable, except: except);
            }
        }

        private static Transform Puppet(ulong owner)
        {
            var ps = Plugin.Instance.Host.Players;
            if (ps == null || !ps.Remotes.TryGetValue(owner, out var rp)
                || rp.Root == null || !rp.Root.activeInHierarchy)
            {
                return null;
            }

            return rp.Root.transform;
        }

        private void OnSounds(ulong from, PacketReader r)
        {
            var (said, n) = PlayerSounds.ReadHead(r);
            ulong owner = Owner(from, said);
            if (PlayerFxRules.TakesSounds(owner, n) != FxDrop.None)
            {
                return;
            }

            var puppet = Puppet(owner);
            bool near = PlayerFxRules.Hears(HearOf(puppet));
            if (puppet != null && !near)
            {
                var key = $"far {owner}";
                var name = _s.NameOf(owner);
                var position = puppet.position;
                string ourPosition = "nowhere";
                if (Player.Instance != null)
                {
                    ourPosition = Player.Instance.transform.position.ToString();
                }

                LogOnce(
                    key,
                    $"[fx] {name}'s sounds: too far away to hear (puppet at "
                        + $"{position}, we're at {ourPosition})");
            }

            _relayed.Clear();
            for (int i = 0; i < n; i++)
            {
                PlayedSound sound = PlayerSounds.ReadSound(r);
                string id = sound.Id;
                float volume = PlayerFxRules.Volume(sound.Volume);
                _relayed.Add(new PlayedSound { Id = id, Volume = volume });

                if (near)
                {
                    AudioController.Play(id, puppet, volume);
                }

                if (near && i == 0)
                {
                    LogOnce(
                        $"sounds {owner}",
                        $"[fx] {_s.NameOf(owner)}'s sounds play on their "
                            + $"puppet ({id})");
                }
            }

            var relay = Relay(MsgType.PlayerSounds);
            if (relay != null)
            {
                PlayerSounds.Write(relay, owner, _relayed);
            }

            Relayed(relay, from, reliable: false);
        }

        private void OnShot(ulong from, PacketReader r)
        {
            var (said, shot) = PlayerShot.Read(r);
            ulong owner = Owner(from, said);
            if (PlayerFxRules.TakesShot(owner) != FxDrop.None)
            {
                return;
            }

            string muzzle = shot.Muzzle, particles = shot.Particles;
            byte flash = shot.Flash;
            var offset =
                new Vector3(shot.Offset.X, shot.Offset.Y, shot.Offset.Z);

            var relay = Relay(MsgType.PlayerShot);
            if (relay != null)
            {
                PlayerShot.Write(relay, owner, shot);
            }

            Relayed(relay, from, reliable: false);

            var t = Puppet(owner);
            if (t == null)
            {
                return;
            }

            ShotsSeen++;
            // As Player.fireWeapon places them on our own player.
            Vector3 at = t.position + t.up * offset.y + t.right * offset.x;
            var rot = Quaternion.Euler(90f, t.eulerAngles.y, 0f);
            if (muzzle != "")
            {
                Pooled(muzzle, at, rot);
            }

            if (particles != "")
            {
                Pooled(particles, at, rot);
            }

            if (flash != 0)
            {
                Core.AddPrefab(
                    "FX/Muzzle/PistolFlash", t.position + t.up, rot, null,
                    worldSpace: true);
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[fx] {_s.NameOf(owner)} fired ({muzzle})");
            }
            else
            {
                LogOnce(
                    $"shot {owner}",
                    $"[fx] {_s.NameOf(owner)}'s shots flash on their puppet");
            }
        }

        private void OnHurt(ulong from, PacketReader r)
        {
            var (said, hurt) = PlayerHurt.Read(r);
            ulong owner = Owner(from, said);
            byte what = hurt.Kind;
            string a = hurt.Prefab, b = hurt.Sound;
            float dy = hurt.Height, yaw = hurt.Yaw;

            if (PlayerFxRules.TakesHurt(what, owner) != FxDrop.None)
            {
                return;
            }

            var relay = Relay(MsgType.PlayerHurt);
            if (relay != null)
            {
                PlayerHurt.Write(relay, owner, hurt);
            }

            Relayed(relay, from, PlayerFxRules.RelaysReliably((HurtKind)what));

            string who = _s.NameOf(owner);
            var ps = Plugin.Instance.Host.Players;
            RemotePlayer rp = null;
            if (ps == null || !ps.Remotes.TryGetValue(owner, out rp))
            {
                return;
            }

            if (what == (byte)HurtKind.Blood)
            {
                var t = Puppet(owner);
                // Only the game's blood splats: a peer doesn't get to spawn any
                // prefab it names.
                if (t == null || !PlayerFxRules.ShowsBlood(a))
                {
                    return;
                }

                Core.AddPrefab(
                    a, t.position + Vector3.up * PlayerFxRules.BloodHeight(dy),
                    Quaternion.Euler(90f, yaw, 0f), null);
                Plugin.Log.LogInfo(
                    $"[fx] {who} bled ({a}): shown on their puppet");
            }
            else if (what == (byte)HurtKind.Burning)
            {
                rp.ShowBurning(a, b);
                Plugin.Log.LogInfo(
                    $"[fx] {who} is burning: flames on their puppet ({a})");
            }
            else
            {
                rp.StopBurning();
                Plugin.Log.LogInfo($"[fx] {who} stopped burning");
            }
        }

        private static void Pooled(string name, Vector3 at, Quaternion rot)
        {
            try
            {
                Core.AddPooledPrefab("FX", name, at, rot);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[fx] no \"{name}\" in the FX pool here: {e.Message}");
            }
        }

        private static HearFacts HearOf(Transform puppet)
        {
            var p = Player.Instance;
            if (puppet == null || p == null)
            {
                return new HearFacts
                {
                    HasPuppet = puppet != null,
                    HasPlayer = p != null,
                    InWorld = WorldShare.InWorld,
                };
            }

            return new HearFacts
            {
                HasPuppet = true,
                HasPlayer = true,
                InWorld = WorldShare.InWorld,
                Ours = VecOf(p.transform.position),
                Puppet = VecOf(puppet.position),
            };
        }

        private SoundSourceFacts SoundOf(Transform parent)
        {
            var p = Player.Instance;
            bool ours = false;
            if (p != null && parent != null)
            {
                ours = parent == p.transform || parent.IsChildOf(p.transform);
            }

            return new SoundSourceFacts
            {
                HasPlayer = p != null,
                Replaying = Coop.Guards.Replaying,
                HasParent = parent != null,
                ParentIsOurs = ours,
                InPlayerCode = _inPlayerCode,
                Depth = Coop.Guards.Depth,
            };
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private void LogOnce(string key, string line)
        {
            if (_logged.Add(key))
            {
                Plugin.Log.LogInfo(line);
            }
        }

        // ---- telling which sounds are our player's --------------------------

        /// <summary>
        /// Inside one of Player's own methods that play its sounds without a
        /// place (at the listener).
        /// </summary>
        private int _inPlayerCode;
        /// <summary>
        /// Inside Player.getHit: the blood splats it makes are ours.
        /// </summary>
        private int _inGetHit;

        /// <summary>Player's sounding code started (PlayerSoundCode).</summary>
        public void EnterPlayerCode()
        {
            _inPlayerCode++;
        }

        public void LeavePlayerCode()
        {
            _inPlayerCode = PlayerFxRules.Leave(_inPlayerCode);
        }

        /// <summary>Player.getHit started (PlayerGetHitBlood).</summary>
        public void EnterGetHit()
        {
            _inGetHit++;
        }

        public void LeaveGetHit()
        {
            _inGetHit = PlayerFxRules.Leave(_inGetHit);
        }

        private bool IsOurs(Transform parent)
        {
            // A sound without a place, played by our player's own code (a
            // gunshot, a reload, a swing, pain). Not while a shared world
            // action runs inside it: that thing's own sounds come from its
            // replay elsewhere.
            return PlayerFxRules.IsOurs(SoundOf(parent));
        }
    }
}
