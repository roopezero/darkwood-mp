using DarkwoodMP.Combat.Noise;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: the noise a client's character made (PlayerNoise), raised here
    /// for that player's proxy, so the host's enemies hear remote players
    /// too. The session's (CoopSession.Noise).
    /// </summary>
    internal sealed class RemoteNoise
    {
        private readonly Session _session;

        internal RemoteNoise(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.PlayerNoise, OnRemoteNoise);
        }

        /// <summary>
        /// Host: a client's character made noise; raise it here, as theirs.
        /// </summary>
        private void OnRemoteNoise(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            SentNoise noise = PlayerNoiseMessage.Read(r);
            NetPlayerProxy proxy;
            NetPlayerProxy.All.TryGetValue(from, out proxy);
            if (!NoiseRules.Raises(FactsOf(proxy, noise)))
            {
                return; // it's meant to be where they are
            }

            Vector3 pos = new Vector3(noise.At.X, noise.At.Y, noise.At.Z);
            var before = Threat.NoiseSource;
            Threat.NoiseSource = proxy;
            try
            {
                Character.alertInArea(
                    pos, NoiseRules.Range(noise.Range), noise.Dangerous,
                    NoiseRules.Volume(noise.Volume), noise.Gunshot);
            }
            finally
            {
                Threat.NoiseSource = before;
            }
        }

        private static RemoteNoiseFacts FactsOf(
            NetPlayerProxy proxy, in SentNoise noise)
        {
            if (proxy == null)
            {
                return new RemoteNoiseFacts { StandIn = false };
            }

            Vector3 at = proxy.transform.position;
            return new RemoteNoiseFacts
            {
                StandIn = true,
                At = new Vec3(at.x, at.y, at.z),
                Noise = noise.At,
            };
        }
    }
}
