using DarkwoodMP.World.Dreaming;
using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Dreams and the chapters held while one runs.</summary>
    public static partial class MapScenarioChapter
    {
        private static Dreams D => Singleton<Dreams>.Instance;
        private static bool Dreaming => Core.EnteringDream
            || (D != null
                && (D.dreaming || D.dreamPrepared || D.switchingDream
                    || (D.currentTransition != null
                        && D.currentTransition.isPlaying)));

        private static IEnumerator OwnDream(string[] a, Result r)
        {
            // A dream of our own only (as the epilogue's startDream step starts
            // one, not a level's: that one is shared).
            string name = "dream_bunker_underground_01";
            if (a.Length > 0)
            {
                name = a[0];
            }

            Core.forbidInputs = true;
            P.halt();
            D.wantToDream = true;
            D.StartCoroutine(D.prepareDream(name));
            float giveUp = Time.realtimeSinceStartup + 40f;
            while (!(D != null && D.dreaming)
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            yield return new WaitForSecondsRealtime(1f);
            Dreams d = D;
            bool? dreaming = null;
            if (d != null)
            {
                dreaming = d.dreaming;
            }

            Dreams dreams = D;
            string presetName = null;
            if (dreams != null)
            {
                DreamPreset preset = dreams.preset;
                if (preset != null)
                {
                    presetName = preset.name;
                }
            }

            string shared = DreamSync.SharedNow;
            if (shared == null)
            {
                shared = "none";
            }

            r.Info =
                $"dreaming {dreaming} ({presetName}), entering "
                    + $"{Core.EnteringDream}, shared {shared}, chapter "
                    + $"{Chapter}";
            if (!(D != null && D.dreaming))
            {
                r.Fail($"not in a dream: {r.Info}");
            }
            else if (DreamSync.SharedNow != null)
            {
                r.Fail($"the dream is shared, not ours: {r.Info}");
            }
        }

        private static IEnumerator Pending(string[] a, Result r)
        {
            float giveUp = Time.realtimeSinceStartup + 20f;
            string line = null, refused = null;
            while (Time.realtimeSinceStartup < giveUp)
            {
                var lines = Lines();
                line = lines.LastOrDefault(
                    l => l.Contains("[notice] The host will start chapter"));
                refused = lines.LastOrDefault(
                    l => l.Contains("[notice] The host didn't start chapter"));
                if (line != null || refused != null)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            string answer = line;
            if (answer == null)
            {
                answer = refused;
            }

            if (answer == null)
            {
                answer = "no answer from the host";
            }

            r.Info = answer.Trim();
            if (refused != null)
            {
                r.Fail($"refused: {r.Info}");
            }
            else if (line == null)
            {
                r.Fail(r.Info);
            }
        }

        private static IEnumerator HeldStep(string[] a, Result r)
        {
            yield return new WaitForSecondsRealtime(5f);
            var lines = Lines();
            string held = lines.LastOrDefault(
                l => l.Contains("which can't run here yet"));
            string heldText = held;
            if (heldText == null)
            {
                heldText = "nothing held";
            }

            r.Info =
                $"{heldText.Trim()}; chapter {Chapter}, generating "
                    + $"{Generating}, dreaming {Dreaming}";
            if (held == null)
            {
                r.Fail($"no chapter held: {r.Info}");
            }
            else if (Chapter != 1 || Generating)
            {
                r.Fail($"the chapter started in our dream: {r.Info}");
            }
            else if (!Dreaming)
            {
                r.Fail($"our dream ended too soon for the test: {r.Info}");
            }
        }
    }
}
