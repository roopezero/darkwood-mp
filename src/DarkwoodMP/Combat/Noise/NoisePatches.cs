using System.Collections.Generic;
using DarkwoodMP.Combat.Noise;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Character.alertInArea is how the game makes noise enemies hear. Raised
    /// from the Player's own code (steps, running, shots), it's that player's
    /// noise: on the host it's threat for them; on a client it's also sent to
    /// the host, which raises it there for that player's proxy, so the host's
    /// enemies hear remote players too.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.alertInArea))]
    public static class PlayerNoisePatch
    {
        private static void Prefix(
            Vector3 _pos, float range, bool dangerousSound, float volume,
            bool gunshot, out CharBase __state)
        {
            __state = Threat.NoiseSource;
            var noise = new SentNoise
            {
                At = new Vec3(_pos.x, _pos.y, _pos.z),
                Range = range,
                Volume = volume,
                Dangerous = dangerousSound,
                Gunshot = gunshot,
            };

            switch (NoiseRules.Owner(FactsOf()))
            {
                case NoiseOwner.OursToHost:
                    var s = Net.Session.Instance;
                    var w = s.Begin(Net.MsgType.PlayerNoise);
                    PlayerNoiseMessage.Write(w, noise);
                    s.SendToHost(w, Net.Channel.Control, reliable: true);
                    Threat.NoiseSource = Player.Instance;
                    return;
                case NoiseOwner.Ours:
                    Threat.NoiseSource = Player.Instance;
                    return;
                default:
                    // Set by whoever raised it (a remote player's noise), or
                    // not our player's.
                    return;
            }
        }

        /// <summary>
        /// The noise as the rule sees it. The stack is walked only when no
        /// source is set and no own-noise method is running.
        /// </summary>
        private static NoiseFacts FactsOf()
        {
            bool set = Threat.NoiseSource != null;
            bool fromPlayer = false;
            if (!set && OwnNoise <= 0)
            {
                fromPlayer = CalledFromPlayer();
            }

            var s = Net.Session.Instance;
            return new NoiseFacts
            {
                Set = set,
                OwnNoise = OwnNoise,
                FromPlayer = fromPlayer,
                Client = s != null && s.IsClient && NetEnemies.ClientMode,
            };
        }

        // A Finalizer rather than a Postfix: it runs when a listener's
        // heardSound throws too, which would otherwise leave NoiseSource set
        // and every later noise counted as this player's (and a client's never
        // sent).
        private static System.Exception Finalizer(
            System.Exception __exception, CharBase __state)
        {
            Threat.NoiseSource = __state;
            return __exception;
        }

        private static bool CalledFromPlayer()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 6; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                System.Type t = null;
                if (frame != null)
                {
                    System.Reflection.MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        t = method.DeclaringType;
                    }
                }

                if (t == null || t == typeof(PlayerNoisePatch)
                    || t == typeof(Character))
                {
                    continue;
                }

                // Player's methods, or its lambdas/iterators
                return t == typeof(Player)
                    || (t.DeclaringType == typeof(Player));
            }

            return false;
        }

        /// <summary>
        /// Above zero inside the game's code that makes noise only for this
        /// game's own player (PlayerNoiseSourcePatch): those methods are
        /// patched too, so their stack frames can't be told apart by type.
        /// </summary>
        public static int OwnNoise;

        /// <summary>
        /// Host: a client's character made noise; raise it here, as theirs.
        /// </summary>
        public static void OnRemoteNoise(ulong from, Net.PacketReader r)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost)
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

    /// <summary>
    /// Noise the game makes only for its own player, outside Player's code: a
    /// door the player opens or closes (Door alerts only with a Player as the
    /// opener), the player's melee hit on a door, window or barricade
    /// (MeleeSensor alerts only for type player), and furniture the player
    /// pushes or drags (ItemSounds checks Player.Instance). Counted as the
    /// player's noise; on a client it's sent to the host like their steps.
    /// </summary>
    [HarmonyPatch]
    static class PlayerNoiseSourcePatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(Door), nameof(Door.open),
                new[] { typeof(Vector3), typeof(Transform), typeof(float) });
            yield return AccessTools.Method(
                typeof(Door), nameof(Door.close), new[] { typeof(Transform) });
            yield return AccessTools.Method(
                typeof(MeleeSensor), "OnTriggerEnter");
            yield return AccessTools.Method(
                typeof(ItemSounds), nameof(ItemSounds.alertCharactersInArea));
        }

        static void Prefix(object __instance, object[] __args, out bool __state)
        {
            __state = NoiseRules.CountsAsOurs(FactsOf(__instance, __args));
            if (__state)
            {
                PlayerNoisePatch.OwnNoise++;
            }
        }

        /// <summary>
        /// The noise maker as the rule sees it: which method from
        /// __instance's type, and its opener or sensor type.
        /// </summary>
        static OwnNoiseFacts FactsOf(object instance, object[] args)
        {
            if (Player.Instance == null)
            {
                return new OwnNoiseFacts { Player = false };
            }

            switch (instance)
            {
                case Door _:
                    int at = NoiseRules.OpenerAt(args.Length);
                    bool opener = at >= 0 && args[at] is Transform t
                        && t != null && t.GetComponent<Player>() != null;
                    return new OwnNoiseFacts
                    {
                        Player = true,
                        Maker = NoiseMaker.Door,
                        PlayerOpener = opener,
                    };

                case MeleeSensor m:
                    return new OwnNoiseFacts
                    {
                        Player = true,
                        Maker = NoiseMaker.Sensor,
                        PlayerSensor =
                            m.type == MeleeSensor.MeleeSensorType.player,
                    };

                case ItemSounds _:
                    // It checks our player itself.
                    return new OwnNoiseFacts
                    {
                        Player = true,
                        Maker = NoiseMaker.Furniture,
                    };

                default:
                    return new OwnNoiseFacts
                    {
                        Player = true,
                        Maker = NoiseMaker.Other,
                    };
            }
        }

        static System.Exception Finalizer(
            System.Exception __exception, bool __state)
        {
            if (__state)
            {
                PlayerNoisePatch.OwnNoise--;
            }

            return __exception;
        }
    }
}
