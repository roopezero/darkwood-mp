using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// For tests on Linux: moves the system's real mouse pointer with ydotool
    /// (its daemon's socket at /tmp/.ydotool_socket) and clicks with it, so the
    /// game gets real input events, which made-up ones can't stand in for.
    /// Moves are relative and accelerated, so it moves, reads where the game
    /// sees the mouse, and moves again.
    /// </summary>
    internal static class RealMouse
    {
        public static bool Available => System.IO.File.Exists(
            "/usr/bin/ydotool")
            && System.IO.File.Exists("/tmp/.ydotool_socket");

        private static void Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/ydotool", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                psi.EnvironmentVariables["YDOTOOL_SOCKET"] =
                    "/tmp/.ydotool_socket";
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(3000);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[mouse] ydotool {args}: {e.Message}");
            }
        }

        /// <summary>
        /// Where the game sees the mouse, in screen pixels from the top left
        /// (as IMGUI's screen points).
        /// </summary>
        public static Vector2 Now => new Vector2(
            Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        /// <summary>
        /// Move the pointer to this screen point (pixels from the top left);
        /// done(true) once it's within 1.5 px.
        /// </summary>
        public static IEnumerator MoveTo(
            Vector2 target, Action<bool> done = null)
        {
            for (int i = 0; i < 30; i++)
            {
                var d = target - Now;
                if (d.magnitude <= 1.5f)
                {
                    if (done != null)
                    {
                        done(true);
                    }

                    yield break;
                }

                // Accelerated moves overshoot the long way: go half of it, then
                // the rest in small steps.
                float k = 1f;
                if (d.magnitude > 40f)
                {
                    k = 0.5f;
                }

                int dx = Mathf.RoundToInt(d.x * k),
                    dy = Mathf.RoundToInt(d.y * k);
                if (dx == 0 && dy == 0)
                {
                    dx = (int)Mathf.Sign(d.x);
                    dy = (int)Mathf.Sign(d.y);
                }

                Run($"mousemove -x {dx} -y {dy}");
                yield return new WaitForSecondsRealtime(0.12f);
                if (i < 3 || i % 10 == 9)
                {
                    Plugin.Log.LogInfo(
                        $"[mouse] to {target}: moved {dx} {dy}, now at {Now}");
                }
            }

            var left = target - Now;
            Plugin.Log.LogInfo(
                $"[mouse] couldn't reach {target}: at {Now} "
                    + $"({left.magnitude:0.0} px away)");
            if (done != null)
            {
                done(left.magnitude <= 3f);
            }
        }

        /// <summary>
        /// A left click where the pointer is: down, a few frames, up.
        /// </summary>
        public static IEnumerator Click()
        {
            Run("click 0x40");
            yield return null;
            yield return null;
            yield return null;
            Run("click 0x80");
            yield return null;
            yield return null;
            yield return null;
        }
    }
}
