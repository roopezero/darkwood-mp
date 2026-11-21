using System.Collections;
using System.IO;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.World.Spectating;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Permadeath and the spectator's view.</summary>
    public static partial class MapScenarioStatus
    {
        /// The session's spectator (null with no session).
        private static Spectator Watcher()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return null;
            }

            return coop.Spectator;
        }

        /// Whether our player is watching the others.
        private static bool Spectating()
        {
            Spectator watcher = Watcher();
            return watcher != null && watcher.Active;
        }

        /// <summary>"permadeath" (after the dumps): our player dies on
        /// Nightmare, which is for good; with the host still playing, we watch
        /// them instead of the permadeath screen.</summary>
        private static IEnumerator Permadeath(string[] a, Result r)
        {
            if (Core.currentProfile == null)
            {
                r.Fail("no profile");
                yield break;
            }

            var was = Core.currentProfile.difficulty;
            Core.currentProfile.difficulty = GameProfile.Difficulty.nightmare;
            P.die();
            float until = Time.realtimeSinceStartup + 15f;
            while (!Spectating() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (!Spectating())
            {
                Core.currentProfile.difficulty = was;
                r.Fail(
                    $"not spectating (alive {P.alive}, permadeath box "
                        + $"{Singleton<UI>.Instance.permadeathBox != null})");
                yield break;
            }

            yield return new WaitForSeconds(2f);
            var cam = Singleton<CamMain>.Instance;
            var host = Other();
            bool follows = cam != null && host != null && host.Root != null
                && cam.followTarget == host.Root.transform;
            float dist = -1f;
            if (host != null && host.Root != null)
            {
                dist = StateDump.Flat(
                    P.transform.position - host.Root.transform.position);
            }

            bool touching =
                P.GetComponentsInChildren<Collider>().Any(c => c.enabled);
            string name = null;
            if (host != null)
            {
                name = host.Name;
            }

            r.Info =
                $"watching {name}: camera follows {follows}, our body {dist:0} "
                    + $"from them, colliders on {touching}, profile dead "
                    + $"{Core.currentProfile.dead}, time scale "
                    + $"{Time.timeScale}";
            if (!follows || dist < 0f || dist > 50f || touching
                || !Core.currentProfile.dead || Time.timeScale == 0f)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"spectator-view" [DIR] (client, spectating): the view cone
        /// is open all around (both lights at 360 degrees, currentFOV 370), and
        /// stays so after the watched player turns; with DIR, a screenshot
        /// DIR/spectator-view.png.</summary>
        private static IEnumerator SpectatorView(string[] a, Result r)
        {
            if (!Spectating())
            {
                r.Fail("not spectating");
                yield break;
            }

            var first = Watcher().ConeAngles();
            yield return new WaitForSeconds(2f);
            var later = Watcher().ConeAngles();
            if (a.Length > 0)
            {
                Directory.CreateDirectory(a[0]);
                string path = Path.Combine(a[0], "spectator-view.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(1f);
                Plugin.Log.LogInfo($"[shots] {path}");
            }

            r.Info =
                $"cone: light {first.light:0}/{later.light:0}, logic "
                    + $"{first.logic:0}/{later.logic:0}, currentFOV "
                    + $"{first.current:0}/{later.current:0} (2 s apart)";
            if (first.light < 360f || first.logic < 360f
                || first.current < 360f || later.light < 360f
                || later.logic < 360f || later.current < 360f)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"spectate-end" (client, spectating, last): spectating ends
        /// as it does when the world is replaced, and the view cone is back at
        /// the angles it had when it began (not 360).</summary>
        private static IEnumerator SpectateEnd(string[] a, Result r)
        {
            if (!Spectating())
            {
                r.Fail("not spectating");
                yield break;
            }

            var saved = Watcher().SavedCone;
            Watcher().TestEnd();
            yield return null;
            var now = Watcher().ConeAngles();
            r.Info =
                $"saved light {saved.light:0}, logic {saved.logic:0}, "
                    + $"currentFOV {saved.current:0}; now light {now.light:0}, "
                    + $"logic {now.logic:0}, currentFOV {now.current:0}; "
                    + $"spectating {Spectating()}";
            if (Spectating() || float.IsNaN(saved.light)
                || now.light != saved.light || now.logic != saved.logic
                || now.current != saved.current)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"sees-spectator" (host): the other player says it's
        /// spectating, isn't shown, and its proxy touches nothing; no movement
        /// corrections were sent for it.</summary>
        private static IEnumerator SeesSpectator(string[] a, Result r)
        {
            int from = MapScenarioIssues.LogMark();
            yield return new WaitForSeconds(3f);
            var rp = Other();
            if (rp == null)
            {
                r.Fail("no other player");
                yield break;
            }

            NetPlayerProxy.All.TryGetValue(rp.Owner, out var proxy);
            Collider col = null;
            if (proxy != null)
            {
                col = proxy.GetComponent<Collider>();
            }

            int corrections = MapScenarioIssues.LogLines().Skip(from)
                .Count(l => l.Contains("put back"));
            bool spectating = rp.Spectating;
            bool shown = rp.Root != null && rp.Root.activeSelf;
            bool? alive = null;
            if (proxy != null)
            {
                alive = proxy.alive;
            }

            r.Info =
                $"{rp.Name}: spectating {spectating}, shown {shown}, proxy "
                    + $"alive {alive}, collider on "
                    + $"{col != null && col.enabled}, corrections "
                    + $"{corrections}";
            if (!rp.Spectating || (rp.Root != null && rp.Root.activeSelf)
                || (col != null && col.enabled) || corrections > 0)
            {
                r.Fail(r.Info);
            }
        }
    }
}
