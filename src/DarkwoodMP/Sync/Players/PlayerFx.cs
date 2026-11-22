using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// What the local player sounds and looks like to the others, beyond the
    /// sprites PlayerSync sends: the sounds the game plays on our player
    /// (steps, shots, reloads, hammering, pain), a gun's muzzle flash, the
    /// blood when we're hurt and the flames while we burn. Each player's game
    /// sends its own; the host passes a client's on to the others (except the
    /// sender), and every game shows it on that player's puppet. It's
    /// presentation only: nothing in it changes the world, so the host has
    /// nothing to check. This is the sending side; PuppetFx shows the
    /// others'.
    /// </summary>
    internal sealed class PlayerFx
    {
        private readonly Session _s;
        // our own: these are sent from inside game calls
        private readonly PacketWriter _w = new PacketWriter(new BepInLog());
        private readonly List<PlayedSound> _sounds = new List<PlayedSound>();
        private Seconds _nextSounds;
        private bool _burningSent;
        private readonly HashSet<string> _logged = new HashSet<string>();

        public PlayerFx(CoopSession coop)
        {
            _s = coop.Net;
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
