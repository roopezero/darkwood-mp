using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Checks of slow motion in co-op.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 58: slow motion ------------------------------------------------

        /// <summary>"slowmo": in co-op, the skill wheel's slow motion
        /// (Core.setTimeScale 0.2), a scripted event's
        /// (Controller.alterTimeScale) and the wheel itself leave the time
        /// scale at 1.</summary>
        private static IEnumerator Slowmo(string[] a, Result r)
        {
            var parts = new List<string>();
            float min = 1f;
            Core.setTimeScale(0.2f);
            yield return null;
            min = Mathf.Min(min, Time.timeScale);
            parts.Add(
                FormattableString.Invariant(
                    $"setTimeScale(0.2): {Time.timeScale:0.00}"));
            var ctl = Singleton<Controller>.Instance;
            ctl.StartCoroutine(ctl.alterTimeScale(2f, 0.2f));
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until)
            {
                min = Mathf.Min(min, Time.timeScale);
                yield return null;
            }

            parts.Add(
                FormattableString.Invariant(
                    $"alterTimeScale: lowest {min:0.00}"));
            var wheel = Singleton<UI>.Instance.activeSkillsMenu;
            // The wheel opens only with a skill to pick: lend it one of the
            // game's while it's open.
            PlayerSkill lent = null;
            if (P.skills != null && P.skills.manuallyActivatedSkills.Count == 0)
            {
                lent = P.skills.availableSkills.Concat(P.skills.skills)
                    .Concat(P.skills.progressionSkills)
                    .FirstOrDefault(sk => sk != null);
                if (lent != null)
                {
                    P.skills.manuallyActivatedSkills.Add(lent);
                }
            }

            if (wheel != null && P.skills != null
                && P.skills.manuallyActivatedSkills.Count > 0)
            {
                wheel.show();
                until = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < until)
                {
                    min = Mathf.Min(min, Time.timeScale);
                    yield return null;
                }

                parts.Add(
                    FormattableString.Invariant(
                            $"the wheel open {wheel.opened}: ")
                        + FormattableString.Invariant(
                            $"{Time.timeScale:0.00}"));
                if (wheel.opened)
                {
                    wheel.hide();
                }

                if (lent != null)
                {
                    P.skills.manuallyActivatedSkills.Remove(lent);
                }
            }
            else
            {
                PlayerSkills skills = P.skills;
                int? count = null;
                if (skills != null)
                {
                    count = skills.availableSkills.Count;
                }

                PlayerSkills playerSkills = P.skills;
                int? knownCount = null;
                if (playerSkills != null)
                {
                    knownCount = playerSkills.skills.Count;
                }

                parts.Add(
                    $"no skills to lend ({count} available, {knownCount} "
                        + "known): the wheel wasn't opened");
            }

            if (Time.timeScale != 1f && Time.timeScale > 0f)
            {
                Core.setTimeScale(1f);
            }

            r.Info = string.Join("; ", parts);
            if (min < 0.99f)
            {
                r.Fail($"time slowed in co-op: {r.Info}");
            }
        }
    }
}
