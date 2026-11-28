using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DarkwoodMP.World.Dreaming;
using DarkwoodMP.World.Places;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for the village cellar's dream in
    /// chapter 2 (tools/tests/map_scenario_basement.py). Diving in the
    /// cellar starts it (GameEvent startDream dream_village_cellar) in the
    /// diver's game; every player must be in the same dream, the host's,
    /// with its doors and events shared, and its end takes everyone out.
    /// </summary>
    public static partial class MapScenarioBasement
    {
        private static Dreams D => Singleton<Dreams>.Instance;

        public static void Register()
        {
            Add("basement-presets", Presets);
            Add("basement-steps", DreamSteps);
            Add("basement-tank", Tank);
            Add("basement-start", Start);
            Add("basement-in", In, timeout: 120f);
            Add("basement-scan", Scan, timeout: 30f);
            Add("basement-door", OpenDoor, timeout: 30f);
            Add("basement-door-is", DoorIs, timeout: 20f);
            Add("basement-fire", Fire, timeout: 30f);
            Add("basement-fired", Fired, timeout: 20f);
            Add("basement-end", End, timeout: 90f);
            Add("basement-out", Out, timeout: 90f);
        }

        /// <summary>
        /// "basement-presets": log every dream preset (name, whether it's
        /// drawn at random, its outcomes).
        /// </summary>
        private static IEnumerator Presets(string[] a, Result r)
        {
            var sb = new StringBuilder();
            foreach (var p in D.allPresets)
            {
                if (p == null)
                {
                    continue;
                }

                string outs = string.Join(
                    ",", p.outcomes.Select(o => o.name));
                sb.Append(
                    $"\n  {p.name} random={p.isRandomDream} "
                        + $"ch2={p.chapter2} diveOut={p.endDivingOut} "
                        + $"after={p.uniqueObjectToTransportToAfterDreamEnd} "
                        + $"outcomes=[{outs}]");
            }

            Plugin.Log.LogInfo($"[basement] presets:{sb}");
            r.Info = $"{D.allPresets.Count} presets (in the log)";
            yield break;
        }

        /// <summary>
        /// "basement-steps": log every startDream and endDream step of the
        /// GameEvents loaded now.
        /// </summary>
        private static IEnumerator DreamSteps(string[] a, Result r)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var g in Object.FindObjectsOfType<GameEvents>(true))
            {
                if (g == null || g.events == null)
                {
                    continue;
                }

                foreach (var e in g.events)
                {
                    bool dream = e != null
                        && (e.type == GameEvent.Type.startDream
                            || e.type == GameEvent.Type.endDream);
                    if (!dream)
                    {
                        continue;
                    }

                    n++;
                    sb.Append(
                        $"\n  {g.transform.root.name}/{g.name}: {e.type} "
                            + $"'{e.Value}' mod={e.activeModifier}");
                }
            }

            Plugin.Log.LogInfo($"[basement] dream steps:{sb}");
            r.Info = $"{n} dream steps (in the log)";
            yield break;
        }

        /// <summary>
        /// "basement-tank": our player carries a full oxygen tank, which the
        /// cellar's dive asks for.
        /// </summary>
        private static IEnumerator Tank(string[] a, Result r)
        {
            var bag = Player.Instance.Inventory;
            bag.addItemTypeToPlayer(SharedTank.Item, 1, dropIfNoRoom: true);
            yield return new WaitForSeconds(1f);
            r.Info = $"carried {MapScenarioWorld.CarriedOf(SharedTank.Item)}";
        }

        /// <summary>
        /// "basement-start" (either): our game runs the dive's dream step
        /// itself (startDream dream_village_cellar through the transition),
        /// as the cellar's first dive does; its once-only events have run
        /// already.
        /// </summary>
        private static IEnumerator Start(string[] a, Result r)
        {
            var step = new GameEvent
            {
                type = GameEvent.Type.startDream,
                Value = StoryDreamRules.CellarDream,
                activeModifier = true,
                eventAmount = 1,
            };

            Singleton<Controller>.Instance.StartCoroutine(
                step.fire(Player.Instance.gameObject));
            yield return new WaitForSeconds(1f);
            r.Info = $"wantToDream {D.wantToDream}";
        }

        private static string PresetName()
        {
            if (D.preset == null)
            {
                return null;
            }

            return D.preset.name;
        }

        private static string SharedName()
        {
            string shared = Coop.Current.Dreaming.Shared;
            if (shared == null)
            {
                return "none";
            }

            return shared;
        }

        /// <summary>
        /// "basement-in" (either): we are dreaming the cellar's dream, and
        /// it is the shared one.
        /// </summary>
        private static IEnumerator In(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 100f;
            while (Time.realtimeSinceStartup < until
                && !(D.dreaming && !D.switchingDream
                    && PresetName() == StoryDreamRules.CellarDream
                    && Coop.Current.Dreaming.Shared
                        == StoryDreamRules.CellarDream))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(3f);
            string at = "none";
            if (D.dreamLocation != null)
            {
                at = Fmt(D.dreamLocation.transform.position);
            }

            r.Info =
                $"dreaming {D.dreaming} {PresetName()} (shared "
                    + $"{SharedName()}), the dream at {at}, we stand at "
                    + $"{Fmt(Player.Instance.transform.position)}";
            if (!D.dreaming || PresetName() != StoryDreamRules.CellarDream
                || Coop.Current.Dreaming.Shared != StoryDreamRules.CellarDream)
            {
                r.Fail($"not in the shared cellar dream: {r.Info}");
            }
        }

        /// <summary>
        /// "basement-end OUTCOME" (either): our dream ends with OUTCOME, as
        /// its end step does; wait until we're back.
        /// </summary>
        private static IEnumerator End(string[] a, Result r)
        {
            if (!D.dreaming)
            {
                r.Fail("not dreaming");
                yield break;
            }

            D.outcome = a[0];
            AsPlayer(() => D.initiateEndDreaming());
            yield return Out(a, r);
        }

        /// <summary>
        /// "basement-out" (either): our dream is over (another game's end
        /// ended it), and no shared dream is left.
        /// </summary>
        private static IEnumerator Out(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 80f;
            while ((D.dreaming || D.switchingDream)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(3f);
            string place = Whereabouts.Current();
            if (place == null)
            {
                place = "the world";
            }

            r.Info =
                $"dreaming {D.dreaming}, at "
                    + $"{Fmt(Player.Instance.transform.position)} in {place}; "
                    + $"shared dream {SharedName()}; inputs forbidden "
                    + $"{Core.forbidInputs}";
            if (D.dreaming || Coop.Current.Dreaming.Shared != null)
            {
                r.Fail($"still in the dream: {r.Info}");
            }
        }

        private static List<T> InDream<T>()
            where T : Component
        {
            var loc = D.dreamLocation;
            if (loc == null)
            {
                return new List<T>();
            }

            return loc.GetComponentsInChildren<T>(true)
                .OrderBy(c => c.name)
                .ThenBy(c => Mathf.Round(Local(c).x))
                .ThenBy(c => Mathf.Round(Local(c).z))
                .ToList();
        }

        private static Vector3 Local(Component c)
        {
            return D.dreamLocation.transform.InverseTransformPoint(
                c.transform.position);
        }
    }
}
