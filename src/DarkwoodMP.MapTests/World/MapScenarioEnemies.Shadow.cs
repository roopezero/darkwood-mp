using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Shadow armour and a joiner's flashlight (P9).</summary>
    public static partial class MapScenarioEnemies
    {
        // ---- P9: shadow armour and a joiner's flashlight --------------------

        private static ShadowArmor _armor;

        private static float _armorHealth;

        /// <summary>
        /// "ee-shadow-spawn" (host): a Dog with shadow armour beside the
        /// client's player, asleep in the dark.
        /// </summary>
        private static IEnumerator ShadowSpawn(string[] a, Result r)
        {
            var proxy = ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }

            var away = proxy.transform.position - P.transform.position;
            away.y = 0f;
            var spot =
                FreeSpot(proxy.transform.position + away.normalized * 300f);
            var c = SpawnAt("Dog", spot, attack: false);
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            // it stays where it is, for the client's beam to find it
            c.immobile = true;
            _armor = c.gameObject.AddComponent<ShadowArmor>();
            _beamLog = "";
            Plugin.Instance.StartCoroutine(BeamWatch(_armor));
            yield return new WaitForSeconds(1.5f);
            _armorHealth = _armor.health;
            bool lit = Core.isInLight(spot, mustBeWalkable: false);
            r.Info =
                $"{c.name} at {Fmt(spot)}, shadow armour {_armorHealth:0}, lit "
                    + $"here {lit}";
            if (lit)
            {
                r.Fail($"its spot is lit already: {r.Info}");
            }
        }

        private static string _beamLog = "";

        /// <summary>
        /// Host, until ee-shadow-check: how the client's beam (as
        /// JoinerBeams sees it) stood to the dog.
        /// </summary>
        private static IEnumerator BeamWatch(ShadowArmor armor)
        {
            int samples = 0, on = 0, inBox = 0, lit = 0;
            string closest = "none";
            float best = float.MaxValue;
            while (armor != null && _armor == armor)
            {
                samples++;
                var proxy = ClientProxy();
                var ps = Plugin.Instance.Host.Players;
                if (Core.isInLight(
                    armor.transform.position, mustBeWalkable: false))
                {
                    lit++;
                }

                if (proxy != null && ps != null
                    && ps.Remotes.TryGetValue(proxy.Owner, out var rp)
                    && rp.Root != null && rp.Last != null && rp.Last.Flashlight)
                {
                    on++;
                    var pos = armor.transform.position;
                    var spot = JoinerBeams.InBeam(
                        rp.Root.transform, pos);
                    float ahead = spot.Ahead;
                    float aside = spot.Aside;
                    if (spot.In)
                    {
                        inBox++;
                    }

                    if (aside < best)
                    {
                        best = aside;
                        Vector3 turn = rp.Root.transform.eulerAngles;
                        closest =
                            FormattableString.Invariant(
                                $"{ahead:0} ahead, {aside:0} aside; ")
                            + FormattableString.Invariant(
                                $"puppet yaw {turn.y:0} rot {turn.x:0}");
                    }
                }

                _beamLog =
                    $"beam watch: {samples} samples, client's beam on in {on}, "
                        + $"dog in its beam in {inBox}, lit in {lit}; nearest: "
                        + $"{closest}";
                yield return new WaitForSeconds(0.25f);
            }
        }

        /// <summary>
        /// "ee-flashlight" (client): our flashlight on, aimed at the host's
        /// shadow-armoured dog for a few seconds, then off.
        /// </summary>
        private static IEnumerator Flashlight(string[] a, Result r)
        {
            var p = P;
            var dog = HostEnemies().Where(e => e.c.name.StartsWith("Dog"))
                .OrderBy(
                    e => Core.trueDistance(
                        e.c.transform.position, p.transform.position))
                .Select(e => e.c).FirstOrDefault();
            if (dog == null)
            {
                r.Fail("no copy of the host's dog here");
                yield break;
            }

            yield return TakeOut(p, "flashlight", r);
            if (r.Failure != null)
            {
                yield break;
            }

            var beam = p.transform.Find("Flashlight");
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until)
            {
                var d = dog.transform.position - p.transform.position;
                d.y = 0f;
                if (d.magnitude > 250f)
                {
                    yield return Walk(dog.transform.position, 160f, detours: 0);
                }

                d = dog.transform.position - p.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1f)
                {
                    p.transform.rotation = Quaternion.Euler(
                        90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
                }

                if (InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != "flashlight")
                {
                    break;
                }

                yield return new WaitForSeconds(0.1f);
            }

            string text = "?";
            if (beam != null)
            {
                if (beam.gameObject.activeSelf)
                {
                    text = "on";
                }
                else
                {
                    text = "off";
                }
            }

            float away = Core.trueDistance(
                dog.transform.position,
                p.transform.position);
            r.Info =
                $"flashlight {text} at {dog.name} "
                    + $"{away:0} away";
            if (beam == null || !beam.gameObject.activeSelf)
            {
                r.Fail($"our flashlight isn't on: {r.Info}");
            }
        }

        /// <summary>"ee-shadow-check [keep]" (host): the client's flashlight
        /// took the shadow armour's health and set the dog on the client. Then
        /// the dog is killed, unless "keep" (ee-armor-burn goes on with
        /// it).</summary>
        private static IEnumerator ShadowCheck(string[] a, Result r)
        {
            if (_armor == null)
            {
                r.Fail("no shadow armour (ee-shadow-spawn first)");
                yield break;
            }

            var c = _armor.GetComponent<Character>();
            CharBase target = null;
            if (c != null && c.target != null)
            {
                target = c.target.GetComponent<CharBase>();
            }

            string text = "nobody";
            if (target is NetPlayerProxy np)
            {
                text = "client " + np.OwnerName;
            }
            else if (target is Player)
            {
                text = "host player";
            }
            else if (target != null)
            {
                text = target.name;
            }

            r.Info =
                $"shadow armour {_armorHealth:0} -> {_armor.health:0}; the dog "
                    + $"goes for {text}";
            bool hurt = _armor.health < _armorHealth;
            r.Info += "; " + _beamLog;
            bool keep = a.Length > 0 && a[0] == "keep";
            if (keep)
            {
                _armoredDog = c;
            }
            else
            {
                _armoredDog = null;
            }

            _armor = null;
            if (keep)
            {
                if (!hurt)
                {
                    r.Fail($"the client's flashlight didn't hurt it: {r.Info}");
                }
                else if (!(target is NetPlayerProxy))
                {
                    r.Fail($"it didn't go for the client: {r.Info}");
                }

                yield break;
            }

            if (c != null)
            {
                Kill(c);
            }

            yield return new WaitForSeconds(1f);
            if (c != null)
            {
                Object.Destroy(c.gameObject);
            }

            if (!hurt)
            {
                r.Fail($"the client's flashlight didn't hurt it: {r.Info}");
            }
            else if (!(target is NetPlayerProxy))
            {
                r.Fail($"it didn't go for the client: {r.Info}");
            }
        }

        private static Character _armoredDog;

        /// <summary>"ee-armor-check on|off" (client): our copy of the host's
        /// shadow-armoured dog wears the armour, with its effect and its health
        /// untouched (no light check or damage on a copy; the client's
        /// flashlight was on it), or wears none (waiting up to 5 s).</summary>
        private static IEnumerator ArmorCheck(string[] a, Result r)
        {
            bool on = a.Length == 0 || a[0] == "on";
            var p = P;
            var dog = HostEnemies().Where(e => e.c.name.StartsWith("Dog"))
                .OrderBy(
                    e => Core.trueDistance(
                        e.c.transform.position, p.transform.position))
                .Select(e => e.c).FirstOrDefault();
            if (dog == null)
            {
                r.Fail("no copy of the host's dog here");
                yield break;
            }

            var dest =
                HarmonyLib.AccessTools.FieldRefAccess<ShadowArmor, float>(
                    "destHealth");
            bool Ok()
            {
                var sa = dog.GetComponent<ShadowArmor>();
                if (!on)
                {
                    return sa == null;
                }

                return sa != null && sa.effectGO != null
                    && sa.health >= sa.maxHealth - 0.01f
                    && dest(sa) >= sa.maxHealth - 0.01f;
            }

            float until = Time.realtimeSinceStartup + 5f;
            while (!Ok() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            var s = dog.GetComponent<ShadowArmor>();
            if (s == null)
            {
                r.Info = $"{dog.name}: no shadow armour";
            }
            else
            {
                string name = dog.name;
                float health = s.health;
                float goal = dest(s);
                float full = s.maxHealth;
                string effect = "none";
                if (s.effectGO != null)
                {
                    effect = "shown";
                }

                r.Info =
                    $"{name}: shadow armour {health:0.#} (going to {goal:0.#}) "
                        + $"of {full:0}, effect {effect}";
            }

            if (!Ok())
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"ee-armor-burn [done]" (host): the dog's shadow armour is
        /// burnt off (as light does: damageMe, then its Update kills it),
        /// waiting until it's gone; "done" kills the dog and removes
        /// it.</summary>
        private static IEnumerator ArmorBurn(string[] a, Result r)
        {
            var c = _armoredDog;
            if (c == null)
            {
                r.Fail(
                    "no shadow-armoured dog kept (ee-shadow-check keep first)");
                yield break;
            }

            if (a.Length > 0 && a[0] == "done")
            {
                _armoredDog = null;
                Kill(c);
                yield return new WaitForSeconds(1f);
                if (c != null)
                {
                    Object.Destroy(c.gameObject);
                }

                yield break;
            }

            var sa = c.GetComponent<ShadowArmor>();
            if (sa == null)
            {
                r.Fail("its shadow armour is gone already");
                yield break;
            }

            sa.damageMe(sa.maxHealth * sa.normalDamageMod * 2f);
            // its Update takes health down 9 a second; this ends it next frame
            sa.health = Mathf.Min(sa.health, 1f);
            float until = Time.realtimeSinceStartup + 5f;
            while (c.GetComponent<ShadowArmor>() != null
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.1f);
            }

            string text = "still on";
            if (c.GetComponent<ShadowArmor>() == null)
            {
                text = "gone";
            }

            r.Info = $"{c.name}: shadow armour {text}, alive {c.alive}";
            if (c.GetComponent<ShadowArmor>() != null)
            {
                r.Fail(r.Info);
            }
        }
    }
}
