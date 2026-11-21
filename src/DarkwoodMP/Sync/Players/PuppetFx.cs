using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// The other players' sounds, muzzle flashes, blood and flames (see
    /// PlayerFx), played and shown on their puppets. The host passes a
    /// client's on to the other clients.
    /// </summary>
    internal sealed class PuppetFx
    {
        /// <summary>
        /// How many of the other players' shots this game has shown on their
        /// puppets in this session (for the map test).
        /// </summary>
        public int ShotsSeen { get; private set; }

        private readonly Session _s;
        private readonly PacketWriter _w = new PacketWriter(new BepInLog());
        private readonly List<PlayedSound> _relayed = new List<PlayedSound>();
        private readonly HashSet<string> _logged = new HashSet<string>();

        public PuppetFx(CoopSession coop)
        {
            _s = coop.Net;
            coop.Handle(MsgType.PlayerSounds, OnSounds);
            coop.Handle(MsgType.PlayerShot, OnShot);
            coop.Handle(MsgType.PlayerHurt, OnHurt);
        }

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
                _w.Begin(t);
                return _w;
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

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private void LogOnce(string key, string line)
        {
            if (_logged.Add(key))
            {
                Plugin.Log.LogInfo(line);
            }
        }
    }
}
