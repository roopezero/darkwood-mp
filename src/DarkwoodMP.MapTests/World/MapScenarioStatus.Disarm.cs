using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Disarming a trap crouched, as seen by both games.</summary>
    public static partial class MapScenarioStatus
    {
        private static Trigger FindTrap(string spec) =>
            MapScenarioWorld.Find<Trigger>(
                spec,
                t => t.active && !t.triggered && t.canDisarm
                    && t.GetComponent<Item>() != null);

        private static string Clip(Player p)
        {
            if (p.torsoAnimator != null && p.torsoAnimator.CurrentClip != null)
            {
                return p.torsoAnimator.CurrentClip.name;
            }

            return "-";
        }

        /// <summary>"disarm-crouch TRAP[@DX,DZ]": start disarming it as the
        /// player does (Player.doDisarm), with the progress bar held, and check
        /// our player crouches (InventoryGet, then InventoryLoop).</summary>
        private static IEnumerator DisarmCrouch(string[] a, Result r)
        {
            var t = FindTrap(a[0]);
            if (t == null)
            {
                r.Fail($"no armed trap {a[0]}");
                yield break;
            }

            yield return Walk(t.transform.position, 90f);
            var item = t.GetComponent<Item>();
            AsPlayer(() => P.doDisarm(item));
            if (P.progressBar != null)
            {
                P.progressBar.time = 60f; // held until disarm-finish
            }

            float until = Time.realtimeSinceStartup + 3f;
            while (Clip(P) != "InventoryLoop"
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            r.Info =
                $"{t.name} at {Fmt(t.transform.position)} (the game's own "
                    + $"crouch: {t.bendDownToDisarm}): clip {Clip(P)}, "
                    + $"disarming {P.disarmingTrap}";
            if (!P.disarmingTrap)
            {
                r.Fail($"{r.Info}: not disarming");
            }
            else if (Clip(P) != "InventoryLoop")
            {
                r.Fail($"{r.Info}: not crouching");
            }
        }

        /// <summary>"puppet-crouch": the other player's puppet here shows the
        /// crouch (its sprites are frames of InventoryGet or InventoryLoop in
        /// the library it says it uses).</summary>
        private static IEnumerator PuppetCrouch(string[] a, Result r)
        {
            var rp = Other();
            if (rp == null)
            {
                r.Fail("no other player");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 4f;
            string seen = "";
            while (Time.realtimeSinceStartup < until)
            {
                var last = rp.Last;
                if (last != null)
                {
                    tk2dSpriteAnimation lib =
                        Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>()
                            .FirstOrDefault(l => l.name == last.Library);
                    if (lib == null)
                    {
                        lib = Resources.Load(
                            last.Library,
                            typeof(tk2dSpriteAnimation)) as tk2dSpriteAnimation;
                    }

                    var frames = new HashSet<(int, int)>();
                    if (lib != null)
                    {
                        foreach (var clip in new[]
                        {
                            "InventoryGet",
                            "InventoryLoop"
                        })
                        {
                            var c = lib.GetClipByName(clip);
                            if (c == null || c.frames == null)
                            {
                                continue;
                            }

                            foreach (var f in c.frames)
                            {
                                if (f.spriteCollection != null)
                                {
                                    string sheet = f.spriteCollection
                                        .spriteCollectionName;
                                    frames.Add(
                                        (Net.StableHash.Of(sheet), f.spriteId));
                                }
                            }
                        }
                    }

                    int match = last.Sprites.Count(
                        s => frames.Contains((s.CollectionHash, s.SpriteId)));
                    string text = "not found here";
                    if (lib != null)
                    {
                        text = "found";
                    }

                    seen =
                        $"library {last.Library} ({text}), {match} of "
                            + $"{last.Sprites.Count} sprites from the crouch, "
                            + "shown "
                            + $"{rp.Root != null && rp.Root.activeInHierarchy}";
                    if (match > 0 && rp.Root != null
                        && rp.Root.activeInHierarchy)
                    {
                        r.Info = $"{rp.Name}: {seen}";
                        yield break;
                    }
                }

                yield return null;
            }

            r.Fail($"{rp.Name} isn't seen crouching: {seen}");
        }

        /// <summary>"disarm-finish TRAP[@DX,DZ]": let the held progress bar
        /// end; the trap is disarmed or sprung (the host's roll) and our player
        /// stands up again.</summary>
        private static IEnumerator DisarmFinish(string[] a, Result r)
        {
            var t = FindTrap(a[0]);
            string what;
            if (t != null)
            {
                what = $"{t.name} at {Fmt(t.transform.position)}";
            }
            else
            {
                what = a[0];
            }

            if (P.progressBar == null || !P.disarmingTrap)
            {
                r.Fail($"not disarming {what} (bar {P.progressBar != null})");
                yield break;
            }

            // the bar ends in its own Update, as the player's action
            Plugin.Instance.Host.TestAsPlayer = true;
            P.progressBar.time = 0.01f;
            float until = Time.realtimeSinceStartup + 4f;
            while (t != null && !t.triggered
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Plugin.Instance.Host.TestAsPlayer = false;
            until = Time.realtimeSinceStartup + 4f;
            while ((P.gettingInventory || P.performingAction)
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            string text = "still armed";
            if (t == null)
            {
                text = "disarmed (gone)";
            }
            else if (t.triggered)
            {
                text = "sprung (the roll failed)";
            }

            r.Info =
                $"{what}: {text}; clip {Clip(P)}, performingAction "
                    + $"{P.performingAction}, gettingInventory "
                    + $"{P.gettingInventory}";
            if (t != null && !t.triggered)
            {
                r.Fail($"{r.Info}: nothing happened");
            }
            else if (P.gettingInventory || P.performingAction)
            {
                r.Fail($"{r.Info}: still crouched");
            }

            if (P.inBearTrap)
            {
                P.stopAllPerformingActionAnims();
            }

            yield return Walk(Centre, 60f);
        }
    }
}
