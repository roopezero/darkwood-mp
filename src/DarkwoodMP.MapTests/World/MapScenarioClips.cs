using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Threading;
using DarkwoodMP.Sync.Players;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps that record what each game shows, for the clips in the
    /// docs (tools/tests/map_scenario_showcase_*.py, tools/make-clips.py).
    ///
    /// "rec-start DIR [FPS [WIDTH]]" starts saving frames into DIR/host or
    /// DIR/c1 as JPG files named by the machine's clock in milliseconds, so
    /// the two games' frames can be paired (both games share that clock;
    /// Time.realtimeSinceStartup starts with each game). It answers at once
    /// and records in the background, through loading screens too, until
    /// "rec-stop". "clip-overlays off|on" hides or shows the test world's
    /// labels and the test's title along the top of the screen.
    /// "clip-panel [SECS]" opens the pause menu and the Multiplayer window
    /// from it for SECS (6) seconds; "clip-join ADDRESS" (a client) goes back
    /// to the main menu as the pause menu does, opens Join Game's server
    /// browser and joins ADDRESS from it, then waits for the world.
    /// "clip-face DX DZ" points
    /// our player's mouse at the centre plus (DX, DZ), so it faces there and
    /// its view shows what's there; "clip-face off" gives the mouse back.
    /// "clip-watch [DIST [SIDE]]" walks to DIST (200) from the other player,
    /// SIDE (0) to the right of the line between us, and keeps facing them
    /// as they move, until "clip-face off".
    /// </summary>
    public static class MapScenarioClips
    {
        private static Recording _rec;

        /// <summary>
        /// False while the clips are recorded: the test's own text is hidden.
        /// </summary>
        public static bool Overlays { get; private set; } = true;

        public static void Register()
        {
            Add("rec-start", Start);
            Add("rec-stop", StopStep, timeout: 60f);
            Add("clip-overlays", SetOverlays);
            Add("clip-panel", Panel, timeout: 60f);
            Add("clip-join", Join, timeout: 240f);
            Add("clip-face", Face);
            Add("clip-watch", WatchStep, timeout: 60f);
        }

        /// <summary>
        /// Ends a recording left running (the test control's reset).
        /// </summary>
        public static void Stop()
        {
            if (_rec != null)
            {
                _rec.Running = false;
            }

            _rec = null;
            Overlays = true;
            _watching = false;
        }

        private static IEnumerator SetOverlays(string[] a, Result r)
        {
            Overlays = a.Length == 0 || a[0] != "off";
            r.Info = $"overlays {Overlays}";
            yield break;
        }

        private static IEnumerator Face(string[] a, Result r)
        {
            _watching = false;
            if (a.Length == 1 && a[0] == "off")
            {
                MapScenarioItems2.AimAt = null;
                r.Info = "the mouse is free";
                yield break;
            }

            if (a.Length < 2)
            {
                r.Fail("usage: clip-face DX DZ | off");
                yield break;
            }

            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            MapScenarioItems2.AimAt = at;
            yield return new WaitForSeconds(0.3f);
            r.Info = $"facing {at.x:0} {at.z:0}";
        }

        private static bool _watching;

        private static IEnumerator WatchStep(string[] a, Result r)
        {
            float dist = 200f;
            if (a.Length > 0)
            {
                dist = F(a[0]);
            }

            float side = 0f;
            if (a.Length > 1)
            {
                side = F(a[1]);
            }

            var other = Other();
            var me = Player.Instance;
            if (other == null || me == null)
            {
                r.Fail("no other player here");
                yield break;
            }

            Vector3 them = other.transform.position;
            Vector3 back = me.transform.position - them;
            back.y = 0f;
            if (back.sqrMagnitude < 1f)
            {
                back = Vector3.back;
            }

            back.Normalize();
            var right = new Vector3(back.z, 0f, -back.x);
            Vector3 to = them + back * dist + right * side;
            yield return Walk(to, 40f);
            _watching = true;
            Plugin.Instance.StartCoroutine(Watch());
            yield return new WaitForSeconds(0.3f);
            float now = StateDump.Flat(them - me.transform.position);
            r.Info = $"watching from {now:0}";
        }

        /// <summary>
        /// Our player's mouse on the other player each frame, while watching.
        /// </summary>
        private static IEnumerator Watch()
        {
            while (_watching)
            {
                var other = Other();
                if (other != null)
                {
                    MapScenarioItems2.AimAt = other.transform.position;
                }

                yield return null;
            }
        }

        /// <summary>
        /// The other player's puppet in this game.
        /// </summary>
        private static GameObject Other()
        {
            var ps = Plugin.Instance.Host.Players;
            if (ps == null)
            {
                return null;
            }

            foreach (var rp in ps.Remotes.Values)
            {
                if (rp != null && rp.Owner != PlayerSync.DummyId
                    && rp.Root != null)
                {
                    return rp.Root;
                }
            }

            return null;
        }

        private static IEnumerator Panel(string[] a, Result r)
        {
            float secs = 6f;
            if (a.Length > 0)
            {
                secs = F(a[0]);
            }

            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1.5f);
            bool took = Plugin.Instance.Host.MenuList.Click("Multiplayer");
            yield return new WaitForSecondsRealtime(secs);
            Plugin.Instance.SetPanel(false);
            yield return new WaitForSecondsRealtime(0.5f);
            Core.showHidePauseMenu();
            r.Info = $"panel shown (entry taken: {took})";
        }

        private static IEnumerator Join(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: clip-join ADDRESS");
                yield break;
            }

            if (!WorldShare.MainMenuReady())
            {
                // Back to the main menu as the pause menu's button does.
                Core.showHidePauseMenu();
                yield return new WaitForSecondsRealtime(1.5f);
                if (Core.mainMenu)
                {
                    Singleton<MainMenu>.Instance.close();
                }

                Core.returnToMainMenu();
            }

            float until = Time.realtimeSinceStartup + 60f;
            while (!WorldShare.MainMenuReady()
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (!WorldShare.MainMenuReady())
            {
                r.Fail("not at the main menu");
                yield break;
            }

            yield return new WaitForSecondsRealtime(2f);
            var host = Plugin.Instance.Host;
            host.MenuList.Click("Join Game");
            yield return new WaitForSecondsRealtime(4f);
            host.Panel.Direct.JoinForTest(a[0]);
            until = Time.realtimeSinceStartup + 200f;
            while (!WorldShare.WorldReady && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (!WorldShare.WorldReady)
            {
                r.Fail("the world didn't load");
                yield break;
            }

            yield return new WaitForSecondsRealtime(4f);
            r.Info = "joined";
        }

        private static IEnumerator Start(string[] a, Result r)
        {
            if (a.Length < 1)
            {
                r.Fail("usage: rec-start DIR [FPS [WIDTH]]");
                yield break;
            }

            if (_rec != null)
            {
                _rec.Running = false;
            }

            var rec = new Recording
            {
                Dir = Path.Combine(a[0], Who()),
            };

            if (a.Length > 1)
            {
                rec.Fps = F(a[1]);
            }

            if (a.Length > 2)
            {
                rec.Width = int.Parse(a[2], CultureInfo.InvariantCulture);
            }

            Directory.CreateDirectory(rec.Dir);
            _rec = rec;
            Plugin.Instance.StartCoroutine(Record(rec));
            r.Info = $"recording {rec.Fps} fps, {rec.Width} wide, {rec.Dir}";
        }

        private static IEnumerator StopStep(string[] a, Result r)
        {
            var rec = _rec;
            if (rec == null)
            {
                r.Fail("not recording");
                yield break;
            }

            rec.Running = false;
            _rec = null;
            float until = Time.realtimeSinceStartup + 30f;
            while (Volatile.Read(ref rec.Writing) > 0
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            double secs = (Now() - rec.First) / 1000.0;
            double fps = 0;
            if (secs > 0)
            {
                fps = rec.Frames / secs;
            }

            r.Info = $"{rec.Frames} frames in {secs:0.0} s ({fps:0.0} fps), "
                + rec.Dir;
        }

        /// <summary>
        /// One frame each 1/FPS s by the clock: the screen after the GUI is
        /// drawn, scaled down to WIDTH, read back and written as a JPG on the
        /// thread pool.
        /// </summary>
        private static IEnumerator Record(Recording rec)
        {
            var end = new WaitForEndOfFrame();
            long step = (long)(1000f / rec.Fps);
            long next = Now();
            rec.First = next;
            RenderTexture full = null;
            RenderTexture small = null;
            Texture2D tex = null;
            try
            {
                while (rec.Running)
                {
                    if (Now() < next)
                    {
                        yield return null;
                        continue;
                    }

                    next += step;
                    if (next < Now())
                    {
                        next = Now() + step;
                    }

                    yield return end;
                    if (!rec.Running)
                    {
                        break;
                    }

                    int w = Screen.width;
                    int h = Screen.height;
                    if (full == null || full.width != w || full.height != h)
                    {
                        Free(full);
                        full = new RenderTexture(w, h, 0);
                    }

                    int sw = rec.Width;
                    int sh = Mathf.RoundToInt((float)h * sw / w) / 2 * 2;
                    if (small == null || small.height != sh)
                    {
                        Free(small);
                        small = new RenderTexture(sw, sh, 0);
                        if (tex != null)
                        {
                            Object.Destroy(tex);
                        }

                        tex = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                    }

                    long stamp = Now();
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                    Scale(full, small);
                    var was = RenderTexture.active;
                    RenderTexture.active = small;
                    tex.ReadPixels(new Rect(0, 0, sw, sh), 0, 0, false);
                    RenderTexture.active = was;
                    byte[] jpg = tex.EncodeToJPG(85);
                    rec.Frames++;
                    string path = Path.Combine(
                        rec.Dir, $"{stamp}-{rec.Frames:00000}.jpg");
                    Write(rec, path, jpg);
                }
            }
            finally
            {
                Free(full);
                Free(small);
                if (tex != null)
                {
                    Object.Destroy(tex);
                }
            }
        }

        /// <summary>
        /// The screen capture comes upside down where the GPU's textures start
        /// at the top (Vulkan, Direct3D): flip it while scaling.
        /// </summary>
        private static void Scale(RenderTexture from, RenderTexture to)
        {
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                Graphics.Blit(
                    from, to, new Vector2(1f, -1f), new Vector2(0f, 1f));
                return;
            }

            Graphics.Blit(from, to);
        }

        private static void Write(Recording rec, string path, byte[] jpg)
        {
            Interlocked.Increment(ref rec.Writing);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    File.WriteAllBytes(path, jpg);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[clips] {path}: {e.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref rec.Writing);
                }
            });
        }

        private static void Free(RenderTexture t)
        {
            if (t != null)
            {
                t.Release();
                Object.Destroy(t);
            }
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// "host", or "c1".."c4" for the client games (Client1..Client4).
        /// </summary>
        private static string Who()
        {
            var s = Plugin.Instance.Host.Session;
            if (s == null || s.IsHost)
            {
                return "host";
            }

            string n = s.LocalName;
            if (n != null && n.StartsWith("Client"))
            {
                return "c" + n.Substring(6);
            }

            return "client";
        }

        private sealed class Recording
        {
            public string Dir;
            public float Fps = 12f;
            public int Width = 640;
            public bool Running = true;
            public int Frames;
            public long First;
            public int Writing;
        }

        [HarmonyPatch(typeof(TestArea), nameof(TestArea.DrawLabels))]
        private static class LabelsPatch
        {
            private static bool Prefix()
            {
                return Overlays;
            }
        }

        [HarmonyPatch(typeof(TestControl), nameof(TestControl.OnGUI))]
        private static class TitlePatch
        {
            private static bool Prefix()
            {
                return Overlays;
            }
        }
    }
}
