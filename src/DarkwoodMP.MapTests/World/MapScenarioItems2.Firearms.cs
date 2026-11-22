using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Firearms fired and reloaded at a dog.</summary>
    public static partial class MapScenarioItems2
    {
        // ---- firearms -------------------------------------------------------

        /// <summary>
        /// "i2-fire TYPE NAME SHOTS": take the gun TYPE out empty, put its
        /// ammunition in our bags and reload it (Player.reload: the game's
        /// reload animation loads it), walk near the NAME dog and fire SHOTS
        /// shots at it (Player.attack), one per the gun's fire rate, reloading
        /// when it's empty. The host must accept every hit (no
        /// "the host refused hit on enemy" here). With --loaded the gun starts
        /// with a full clip and the first reload is skipped: the dog bites
        /// every second or so, each bite interrupts what the player is doing
        /// (Player.interruptAllActions), and a gun with a long reload animation
        /// (the burst rifle) never finishes one.
        /// </summary>
        private static IEnumerator Fire(string[] a, Result r)
        {
            var p = P;
            bool loaded = System.Array.IndexOf(a, "--loaded") >= 0;
            a = System.Array.FindAll(a, s => s != "--loaded");
            string type = a[0];
            string name = "gun";
            if (a.Length > 1)
            {
                name = a[1];
            }

            int shots = 3;
            if (a.Length > 2)
            {
                shots = int.Parse(a[2]);
            }

            Character dog = null;
            yield return WaitDog(name, c => dog = c);
            if (dog == null)
            {
                r.Fail($"no {name} dog here");
                yield break;
            }

            int mark = MapScenarioIssues.LogMark();
            try
            {
                yield return TakeOut(type, r, it =>
                {
                    if (loaded)
                    {
                        it.ammo = it.baseClass.clipSize;
                    }
                    else
                    {
                        it.ammo = 0;
                    }
                });

                if (r.Failure != null)
                {
                    yield break;
                }

                var gun = p.currentItem;
                var b = gun.baseClass;
                if (!b.isFirearm)
                {
                    r.Fail($"a {type} isn't a firearm");
                    yield break;
                }

                GiveBag(b.ammoType, b.clipSize * 2 + 4);
                string loads = "";
                IEnumerator Reload()
                {
                    int before = gun.ammo;
                    float until = Time.realtimeSinceStartup + 15f;
                    // The dog bites while we reload, and a bite
                    // (Player.gettingHit) stops a reload starting: ask again
                    // until it starts, as a player would press the key again.
                    while (!p.reloading && gun.ammo == before
                        && Time.realtimeSinceStartup < until)
                    {
                        AsPlayer(() => ReloadM.Invoke(p, null));
                        yield return new WaitForSeconds(0.3f);
                    }

                    while ((p.reloading || gun.ammo == before)
                        && Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                    }

                    string separator = ", ";
                    if (loads == "")
                    {
                        separator = "";
                    }

                    loads += separator + $"{before}->{gun.ammo}";
                    if (gun.ammo <= before)
                    {
                        int rounds = gun.ammo;
                        int clip = b.clipSize;
                        bool reloading = p.reloading;
                        string kind = b.ammoType;
                        int bagged = 0;
                        InvItemClass spare =
                            p.Inventory.getItemInPlayer(b.ammoType);
                        if (spare != null)
                        {
                            bagged = spare.amount;
                        }

                        r.Fail(
                            $"the {type} didn't reload ({before} -> {rounds} "
                                + $"of {clip}, reloading {reloading}, {kind} "
                                + $"in bags {bagged})");
                    }
                }

                if (loaded)
                {
                    loads = $"loaded {gun.ammo}";
                }
                else
                {
                    yield return Reload();
                }

                if (r.Failure != null)
                {
                    yield break;
                }

                int fired = 0;
                var aimed = new List<string>();
                int moved = 0;
                for (int i = 0; i < shots && dog != null; i++)
                {
                    if (gun.ammo <= 0)
                    {
                        yield return Reload();
                        if (r.Failure != null)
                        {
                            yield break;
                        }
                    }

                    var at = dog.transform.position;
                    if (i == 0
                        || StateDump.Flat(at - p.transform.position) > 260f)
                    {
                        yield return Walk(Stand(at, 180f), 30f);
                    }

                    at = dog.transform.position;
                    AimAt = at;
                    Face(at);
                    // A wall between us and the dog takes every shot: step
                    // round the dog until the line of fire is clear, as a
                    // player would.
                    string first = LineOfFire(p, dog, out bool clear);
                    var round = p.transform.position - at;
                    round.y = 0f;
                    for (int k = 1; k < 6 && !clear && dog != null; k++)
                    {
                        moved++;
                        yield return Walk(
                            MapScenarioEnemies.FreeSpot(
                                dog.transform.position + Quaternion.Euler(
                                    0f, 60f * k, 0f) * round.normalized * 180f),
                            15f);
                        at = dog.transform.position;
                        AimAt = at;
                        Face(at);
                        first = LineOfFire(p, dog, out clear);
                    }

                    aimed.Add(first);
                    int ammo = gun.ammo;
                    // as steady as aiming makes it (Player's aim narrows it to
                    // minAim)
                    p.currentAim = b.minAim;
                    AsPlayer(() => AttackM.Invoke(p, new object[] { 1f }));
                    if (gun.ammo < ammo)
                    {
                        fired++;
                    }

                    yield return new WaitForSeconds(
                        Mathf.Max(b.fireRate, 0.05f) + 0.05f);
                }

                yield return new WaitForSeconds(1.5f); // the host's answers
                var sent =
                    LogSince(mark, @"\[authority\] request \d+: hit on enemy");
                var refused = LogSince(mark, @"the host refused hit on enemy");
                string info =
                    $"{type} ({b.fireMode}, {b.projectileAmount} pellet(s), "
                        + $"every {b.fireRate:0.00} s, clip {b.clipSize}, "
                        + $"{b.ammoType}): "
                        + $"reloads {loads}; {fired} of {shots} fired, "
                        + $"{sent.Count} hit(s) sent to the host, "
                        + $"{refused.Count} refused; ";
                if (dog != null)
                {
                    info += $"{dog.name} health here {dog.health:0}";
                }
                else
                {
                    info += "the dog is gone";
                }

                info += "; first in the line of fire "
                    + string.Join(
                        ", ",
                        aimed.Distinct().Select(
                            x => $"{x} x{aimed.Count(y => y == x)}"));
                if (moved > 0)
                {
                    info +=
                        $" ({moved} step(s) round the dog for a clear shot)";
                }

                r.Info = info;
                if (fired < shots)
                {
                    r.Fail("not every shot fired: " + r.Info);
                }
                else if (refused.Count > 0)
                {
                    r.Fail(r.Info + ": " + refused[0].Trim());
                }
                else if (Plugin.Instance.Host.Session.IsClient
                    && sent.Count == 0)
                {
                    r.Fail("no hit reached the host's dog: " + r.Info);
                }
            }
            finally
            {
                AimAt = null;
                Restore();
            }
            // (We stay there: a dog that's been shot follows the shooter, and
            // back at the centre it ran into the stations' traps.)
        }

        /// <summary>What the shot's ray (Player.spawnBullet: from our player
        /// along its facing) meets first, as "NAME@DISTANCE"; `clear` when
        /// that's the dog.</summary>
        private static string LineOfFire(
            Player p, Character dog, out bool clear)
        {
            clear = false;
            if (!Physics.Raycast(
                p.transform.position + p.transform.up, p.transform.up,
                out var hit, 1000f, 18909185) || hit.collider == null)
            {
                return "nothing";
            }

            var t = hit.collider.transform;
            clear = dog != null
                && (t.GetComponent<Character>() == dog
                    || (t.parent != null
                        && t.parent.GetComponent<Character>() == dog));
            string hitName;
            if (t.parent != null)
            {
                hitName = t.parent.name;
            }
            else
            {
                hitName = t.name;
            }

            return $"{hitName}@{hit.distance:0}";
        }
    }
}
