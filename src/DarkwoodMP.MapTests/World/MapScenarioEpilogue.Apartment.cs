using System.Collections;
using System.Text;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// The epilogue's first part, the player's flat: its triggers, and a
    /// joiner going through them.
    public static partial class MapScenarioEpilogue
    {
        /// <summary>
        /// "epiapt-dump": log every trigger and GameEvents in the current
        /// dream location, its lights, and anything named like a dog.
        /// </summary>
        private static IEnumerator AptDump(string[] a, Result r)
        {
            var root = D.dreamLocation;
            if (root == null)
            {
                r.Fail($"no dream location: {Where()}");
                yield break;
            }

            var sb = new StringBuilder();
            sb.Append($"location {root.name} at {Fmt(root.transform.position)}"
                + $" player at {Fmt(P.transform.position)}");
            foreach (var et in root.GetComponentsInChildren<EventTriggers>(
                true))
            {
                sb.Append($"\n{Path(et.transform)} at "
                    + $"{Fmt(et.transform.position)} "
                    + $"active={et.gameObject.activeInHierarchy}");
                foreach (var q in et.eventRequirements)
                {
                    if (q != null)
                    {
                        sb.Append($"\n    req {MapScenarioDive.Req(q)}");
                    }
                }

                foreach (var t in et.eventTriggers)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    sb.Append($"\n  > {t.type} '{t.reactsToItemType}' "
                        + $"fired={t.fired} multi={t.multipleFire} "
                        + $"disabled={t.disabled}");
                    AppendEvents(sb, "do", t.gameEvents);
                    AppendEvents(sb, "else", t.gameEventsElse);
                    AppendEvents(sb, "exit", t.gameEventsExit);
                }
            }

            foreach (var g in root.GetComponentsInChildren<GameEvents>(true))
            {
                if (g.GetComponent<EventTriggers>() == null)
                {
                    AppendEvents(sb, "events " + Path(g.transform), g);
                }
            }

            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                sb.Append($"\nlight {Path(l.transform)} "
                    + $"on={l.enabled && l.gameObject.activeInHierarchy}");
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.ToLowerInvariant().Contains("dog")
                    || t.name.ToLowerInvariant().Contains("pies"))
                {
                    sb.Append($"\nnamed {Path(t)} at {Fmt(t.position)} "
                        + $"active={t.gameObject.activeInHierarchy}");
                }
            }

            Plugin.Log.LogInfo("[map] epiapt-dump " + sb);
            r.Info = $"dumped {root.name}";
        }

        /// The flat's world event that the wardrobe's dialogue sets off.
        private const string ClothesEvent = "player_changeClothes";

        /// <summary>
        /// "epiapt-clothes" (either game): our player changes clothes at the
        /// wardrobe (its dialogue's world event); our player's clothes change.
        /// </summary>
        private static IEnumerator AptClothes(string[] a, Result r)
        {
            bool before = P.changedClothes;
            AsPlayer(() => Singleton<Events>.Instance.fireWorldEvent(
                ClothesEvent));
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until
                && P.changedClothes == before)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(1f);
            r.Info = $"changed clothes {before} -> {P.changedClothes}, "
                + $"body {LibraryOf(P)}";
            if (!P.changedClothes || !LibraryOf(P).Contains("Epilogue"))
            {
                r.Fail($"our clothes didn't change: {r.Info}");
            }
        }

        private static string LibraryOf(Player p)
        {
            if (p.torsoAnimator == null || p.torsoAnimator.Library == null)
            {
                return "none";
            }

            return p.torsoAnimator.Library.name;
        }

        /// <summary>
        /// "epiapt-puppet-clothes" (either game): the other player's puppet
        /// wears what they changed into.
        /// </summary>
        private static IEnumerator AptPuppetClothes(string[] a, Result r)
        {
            string seen = "no puppet";
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until)
            {
                seen = PuppetLibrary();
                if (seen.Contains("Epilogue"))
                {
                    break;
                }

                yield return new WaitForSeconds(0.2f);
            }

            r.Info = $"the puppet's body: {seen}";
            if (!seen.Contains("Epilogue"))
            {
                r.Fail(r.Info);
            }
        }

        private static string PuppetLibrary()
        {
            var ps = Plugin.Instance.Host.Players;
            if (ps == null)
            {
                return "no players";
            }

            foreach (var rp in ps.Remotes.Values)
            {
                if (rp.Last != null && rp.Root != null)
                {
                    int shown = rp.Root.GetComponentsInChildren<tk2dSprite>()
                        .Length;
                    return $"{rp.Last.Library} ({shown} sprites)";
                }
            }

            return "no puppet";
        }

        /// <summary>
        /// "epiapt-room NAME" (either game): our player walks into the flat's
        /// room NAME (kuchnia, the kitchen; dpokoj, the living room): its mask
        /// lifts (the room lights up) in our game.
        /// </summary>
        private static IEnumerator AptRoom(string[] a, Result r)
        {
            string name = "kuchnia";
            if (a.Length > 0)
            {
                name = a[0];
            }

            var et = Named<EventTriggers>($"ROOM_mask_{name}_event");
            if (et == null || et.eventTriggers.Count == 0)
            {
                r.Fail($"no trigger for {name}: {Where()}");
                yield break;
            }

            GameEvents ge = et.eventTriggers[0].gameEvents;
            GameObject mask = ge.events[0].targetGameObjects[0];
            float before = AlphaOf(mask);
            yield return Walk(et.transform.position, 20f);
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && AlphaOf(mask) > Lit)
            {
                yield return new WaitForSeconds(0.2f);
            }

            float after = AlphaOf(mask);
            r.Info = $"{name}: fired {ge.fired}, the mask's alpha "
                + $"{before:0.00} -> {after:0.00}";
            if (!ge.fired || after > Lit)
            {
                r.Fail($"the room didn't light up: {r.Info}");
            }
        }

        /// A room's mask at most this opaque has lifted (it starts at 1 and
        /// tweens to 0).
        private const float Lit = 0.75f;

        /// The first object of this type and name in the dream location.
        private static T Named<T>(string name)
            where T : Component
        {
            foreach (var c in D.dreamLocation.GetComponentsInChildren<T>(true))
            {
                if (c.name == name)
                {
                    return c;
                }
            }

            return null;
        }

        private static float AlphaOf(GameObject go)
        {
            var sprite = go.GetComponentInChildren<tk2dBaseSprite>(true);
            if (sprite != null)
            {
                return sprite.color.a;
            }

            var renderer = go.GetComponentInChildren<Renderer>(true);
            if (renderer != null && renderer.sharedMaterial != null)
            {
                return renderer.sharedMaterial.color.a;
            }

            return -1f;
        }

        /// <summary>
        /// "epiapt-pet" (either game): our player pets the dog in the living
        /// room and can move again once it's done. "epiapt-pet bare": before
        /// changing clothes, whose body has no PetDog clip: no petting, and
        /// nothing holds our player still.
        /// </summary>
        private static IEnumerator AptPet(string[] a, Result r)
        {
            bool bare = a.Length > 0 && a[0] == "bare";
            var et = Named<EventTriggers>("Dog");
            if (et == null)
            {
                r.Fail($"no dog: {Where()}");
                yield break;
            }

            yield return Walk(et.transform.position, 60f);
            yield return new WaitForSeconds(0.5f);
            AsPlayer(() => et.fireEventTrigger(
                global::EventTrigger.Type.onActivate));
            bool petted = false;
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && !petted)
            {
                petted = P.pettingDog;
                yield return new WaitForSeconds(0.05f);
            }

            until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && Stuck())
            {
                yield return new WaitForSeconds(0.2f);
            }

            r.Info = $"petted {petted}; then performingAction "
                + $"{P.performingAction} pettingDog {P.pettingDog} "
                + $"forbidInputs {Core.forbidInputs} immobilised "
                + $"{P.immobilised}; clip {ClipOf(P)}, body {LibraryOf(P)}";
            if (petted == bare || Stuck())
            {
                r.Fail(r.Info);
            }
        }

        /// Whether our player can't move by itself (Player.Update's checks).
        private static bool Stuck() => P.performingAction || P.pettingDog
            || Core.forbidInputs || P.immobilised;

        private static string ClipOf(Player p)
        {
            if (p.torsoAnimator == null || p.torsoAnimator.CurrentClip == null)
            {
                return "none";
            }

            return p.torsoAnimator.CurrentClip.name;
        }

        private static void AppendEvents(
            StringBuilder sb, string label, GameEvents ge)
        {
            if (ge == null)
            {
                return;
            }

            sb.Append($"\n    {label}: {ge.name} multi={ge.multipleFire} "
                + $"fired={ge.fired}");
            foreach (var e in ge.events)
            {
                if (e != null)
                {
                    sb.Append($"\n      {MapScenarioDive.Ev(e)} "
                        + $"go={e.gameObjectModifyType} "
                        + $"item={e.itemModifyType} int={e.intValue} "
                        + $"float={e.floatValue} show={e.showMe} "
                        + $"fromParent={e.getTargetGameObjectsFromParent}");
                }
            }
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            var at = t.parent;
            while (at != null && at != D.dreamLocation.transform)
            {
                path = at.name + "/" + path;
                at = at.parent;
            }

            return path;
        }
    }
}
