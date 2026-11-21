using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// "--dwmp-profile-mod": what the mod's own per-frame work costs. Each part
    /// of Plugin.Update is timed, and every 10 seconds the log gets each part's
    /// average per frame, the mod's total (average and worst frame) and the
    /// game's average frame. Harmony patches inside the game's code aren't
    /// included.
    /// </summary>
    internal sealed class ModProfiler
    {
        public bool Enabled;
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly Dictionary<string, double> _ms =
            new Dictionary<string, double>();
        private readonly Dictionary<string, double> _worst =
            new Dictionary<string, double>();
        private double _frameMod, _worstMod, _frames;
        private float _frameTime, _since;

        public void Begin()
        {
            if (Enabled)
            {
                _sw.Restart();
            }
        }

        /// <summary>
        /// The time since Begin or the last Mark goes to this part.
        /// </summary>
        public void Mark(string part)
        {
            if (!Enabled)
            {
                return;
            }

            double ms = _sw.Elapsed.TotalMilliseconds;
            _sw.Restart();
            _ms.TryGetValue(part, out double t);
            _ms[part] = t + ms;
            _worst.TryGetValue(part, out double wst);
            if (ms > wst)
            {
                _worst[part] = ms;
            }

            _frameMod += ms;
        }

        public void EndFrame()
        {
            if (!Enabled)
            {
                return;
            }

            _frames++;
            _frameTime += Time.unscaledDeltaTime;
            if (_frameMod > _worstMod)
            {
                _worstMod = _frameMod;
            }

            _frameMod = 0;
            if (Time.unscaledTime - _since < 10f)
            {
                return;
            }

            _since = Time.unscaledTime;
            double total = _ms.Values.Sum();
            string parts = string.Join(
                ", ",
                _ms.OrderByDescending(kv => kv.Value).Take(10)
                    .Select(
                        kv => $"{kv.Key} {kv.Value / _frames:0.000} (worst "
                            + $"{_worst[kv.Key]:0.0})"));
            Plugin.Log.LogInfo(
                $"[profile-mod] {_frames} frames, "
                    + $"{_frameTime / _frames * 1000f:0.0} ms each; the mod "
                    + $"{total / _frames:0.000} ms a frame (worst "
                    + $"{_worstMod:0.00}): {parts}");
            _ms.Clear();
            _worst.Clear();
            _frames = 0;
            _frameTime = 0;
            _worstMod = 0;
        }
    }
}
