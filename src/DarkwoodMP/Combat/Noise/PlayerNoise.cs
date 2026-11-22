using System.Runtime.CompilerServices;
using DarkwoodMP.Combat.Noise;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Our player's noise (CharacterAlertInArea, OwnNoiseMakers). It lives as
    /// long as the game (ModHost.Noise): noise is counted as our player's
    /// when playing alone too.
    /// </summary>
    internal sealed class PlayerNoise
    {
        /// <summary>
        /// Above zero inside the game's code that makes noise only for this
        /// game's own player (OwnNoiseMakers): those methods are patched too,
        /// so their stack frames can't be told apart by type.
        /// </summary>
        public int OwnNoise { get; set; }

        /// <summary>
        /// Character.alertInArea is starting: say whose noise it is
        /// (Threat.NoiseSource), and on a client send our player's to the
        /// host. Returns the source to put back once it's done (Done).
        /// Not inlined: CalledFromPlayer skips this frame by count.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public CharBase Raised(in SentNoise noise)
        {
            CharBase before = Plugin.Instance.Host.Threats.NoiseSource;
            // The stack is walked here, called from the patch's Prefix:
            // CalledFromPlayer skips its own frame, this one and the
            // Prefix's.
            bool fromPlayer = false;
            if (Plugin.Instance.Host.Threats.NoiseSource == null
                && OwnNoise <= 0)
            {
                fromPlayer = CalledFromPlayer();
            }

            switch (NoiseRules.Owner(FactsOf(fromPlayer)))
            {
                case NoiseOwner.OursToHost:
                    var s = Plugin.Instance.Host.Session;
                    var w = s.Begin(Net.MsgType.PlayerNoise);
                    PlayerNoiseMessage.Write(w, noise);
                    s.SendToHost(w, Net.Channel.Control, reliable: true);
                    Plugin.Instance.Host.Threats.NoiseSource = Player.Instance;
                    return before;
                case NoiseOwner.Ours:
                    Plugin.Instance.Host.Threats.NoiseSource = Player.Instance;
                    return before;
                default:
                    // Set by whoever raised it (a remote player's noise), or
                    // not our player's.
                    return before;
            }
        }

        /// <summary>
        /// Character.alertInArea is done, or threw: the noise source it
        /// started with goes back.
        /// </summary>
        public void Done(CharBase before)
        {
            Plugin.Instance.Host.Threats.NoiseSource = before;
        }

        /// <summary>
        /// The noise as the rule sees it. Raised walks the stack only when
        /// no source is set and no own-noise method is running.
        /// </summary>
        private NoiseFacts FactsOf(bool fromPlayer)
        {
            bool set = Plugin.Instance.Host.Threats.NoiseSource != null;
            var s = Plugin.Instance.Host.Session;
            return new NoiseFacts
            {
                Set = set,
                OwnNoise = OwnNoise,
                FromPlayer = fromPlayer,
                Client = s != null && s.IsClient && NetEnemies.ClientMode,
            };
        }

        /// <summary>
        /// Whether the Player's code raised this noise. The frames skipped
        /// by count are this method's, Raised's and the patch's Prefix;
        /// after them, the patch's, this class's and Character's frames are
        /// skipped by type.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool CalledFromPlayer()
        {
            var st = new System.Diagnostics.StackTrace(3, false);
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

                if (t == null || t == typeof(CharacterAlertInArea)
                    || t == typeof(PlayerNoise) || t == typeof(Character))
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
        /// One of OwnNoiseMakers' methods is starting. Returns whether it
        /// counts as our player's noise (then OwnNoise is raised until
        /// LeaveOwn).
        /// </summary>
        public bool EnterOwn(object instance, object[] args)
        {
            bool ours = NoiseRules.CountsAsOurs(FactsOf(instance, args));
            if (ours)
            {
                OwnNoise++;
            }

            return ours;
        }

        /// <summary>One of OwnNoiseMakers' methods is done, or threw.</summary>
        public void LeaveOwn(bool ours)
        {
            if (ours)
            {
                OwnNoise--;
            }
        }

        /// <summary>
        /// The noise maker as the rule sees it: which method from
        /// __instance's type, and its opener or sensor type.
        /// </summary>
        private static OwnNoiseFacts FactsOf(object instance, object[] args)
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
    }
}
