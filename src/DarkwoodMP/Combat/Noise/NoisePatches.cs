using System.Collections.Generic;
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
            if (Threat.NoiseSource != null)
            {
                return; // set by whoever raised it (a remote player's noise)
            }

            if (OwnNoise <= 0 && !CalledFromPlayer())
            {
                return;
            }

            var s = Net.Session.Instance;
            if (s != null && s.IsClient && NetEnemies.ClientMode)
            {
                var w = s.Begin(Net.MsgType.PlayerNoise);
                w.Write(_pos);
                w.W.Write(range);
                w.W.Write(volume);
                int flags = 0;
                if (dangerousSound)
                {
                    flags |= 1;
                }

                if (gunshot)
                {
                    flags |= 2;
                }

                w.W.Write((byte)flags);
                s.SendToHost(w, Net.Channel.Control, reliable: true);
            }

            Threat.NoiseSource = Player.Instance;
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

            Vector3 pos = r.ReadVector3();
            float range = Mathf.Clamp(r.R.ReadSingle(), 0f, 2000f);
            float volume = Mathf.Clamp(r.R.ReadSingle(), 0f, 5f);
            byte f = r.R.ReadByte();
            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy)
                || proxy == null)
            {
                return;
            }

            if ((proxy.transform.position - pos).sqrMagnitude > 500f * 500f)
            {
                return; // it's meant to be where they are
            }

            var before = Threat.NoiseSource;
            Threat.NoiseSource = proxy;
            try
            {
                Character.alertInArea(
                    pos, range, (f & 1) != 0, volume, (f & 2) != 0);
            }
            finally
            {
                Threat.NoiseSource = before;
            }
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
            __state = false;
            var player = Player.Instance;
            if (player == null)
            {
                return;
            }

            switch (__instance)
            {
                case Door _:
                    if (__args.Length > 0)
                    {
                        int index = 0;
                        if (__args.Length > 1)
                        {
                            index = 1;
                        }

                        __state = __args[index] is Transform t && t != null
                            && t.GetComponent<Player>() != null;
                    }
                    else
                    {
                        __state = false;
                    }

                    break;
                case MeleeSensor m:
                    __state = m.type == MeleeSensor.MeleeSensorType.player;
                    break;
                case ItemSounds _:
                    __state = true; // it checks our player itself
                    break;
            }

            if (__state)
            {
                PlayerNoisePatch.OwnNoise++;
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
