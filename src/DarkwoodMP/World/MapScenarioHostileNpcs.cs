using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for hostile NPCs (gap 33, Combat.HostileNpcs): an NPC put
    /// in the test area before the join (so it's in the save the client loads)
    /// is friendly in both games; the client's player hits it, the host's copy
    /// turns on the client's stand-in and the host runs it as an enemy, the
    /// client's copy shows the host's; it attacks the client, both players'
    /// hits go into the one health, and it dies in both games. A second NPC is
    /// sent after the client by a scripted attack in the client's game (as a
    /// dialogue's outcome does). The order is
    /// tools/tests/map_scenario_hostile.py.
    /// </summary>
    public static class MapScenarioHostileNpcs
    {
        private static Player P => Player.Instance;
        /// <summary>
        /// Our health as we first hit the NPC (npc-hit), for npc-attacks-us.
        /// </summary>
        private static float _healthAtHit = -1f;

        public static void Register()
        {
            Add("npc-spawn", Spawn);
            Add("npc-friendly", Friendly);
            Add("npc-hit", Hit, timeout: 30f);
            Add("npc-host-runs", HostRuns, timeout: 20f);
            Add("npc-attacks-us", AttacksUs, timeout: 45f);
            Add("npc-provoke", Provoke, timeout: 20f);
            Add("npc-kill", Kill, timeout: 90f);
            Add("npc-dead", Dead, timeout: 20f);
            Add("npc-keep-away", KeepAway, timeout: 30f);
        }

        /// <summary>
        /// "npc-keep-away NAME DIST": our player walks to DIST from the NPC, on
        /// our side of it (out of its fight).
        /// </summary>
        private static IEnumerator KeepAway(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            float dist = F(a[1]);
            var away = P.transform.position - c.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1f)
            {
                away = Vector3.right;
            }

            var to = c.transform.position + away.normalized * dist;
            yield return Walk(to, 60f);
            r.Info =
                $"at {Fmt(P.transform.position)}, "
                    + $"{StateDump.Flat(P.transform.position - c.transform.position):0} from {a[0]}";
        }

        /// <summary>
        /// The story flags with this in their name, for the log ("name=true").
        /// </summary>
        private static string FlagsWith(string part)
        {
            var f = Singleton<Flags>.Instance;
            if (f == null)
            {
                return "-";
            }

            var dict =
                HarmonyLib.AccessTools
                    .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                        "flagsDict")(f);
            return string.Join(
                " ", dict.Values.Where(
                    x => x != null
                        && x.name.IndexOf(
                            part, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(x => $"{x.name}={x.isTrue}")
                    .OrderBy(x => x, StringComparer.Ordinal));
        }

        private static string Kind(Character c) => Combat.NetEnemies.KindOf(c);

        /// <summary>
        /// The NPC of this kind nearest the centre, in this game.
        /// </summary>
        private static Character Npc(string kind) =>
            Object.FindObjectsOfType<Character>().Where(
                c => c != null && c.npc != null && Kind(c) == kind)
                .OrderBy(c => StateDump.Flat(c.transform.position - Centre))
                .FirstOrDefault();

        private static string Props(Character c)
        {
            string head =
                $"{c.name} ({Kind(c)}, npc {c.npc.name}) at "
                    + $"{Fmt(c.transform.position)}: health "
                    + $"{c.health:0}/{c.maxHealth:0}, alive {c.alive}, " +
                          $"{c.aggressiveness}, {c.behaviour}, faction "
                    + $"{c.faction}, attacks players "
                    + $"{c.attacksFaction(Faction.player)}, ";
            string target = "none";
            if (c.target != null)
            {
                target = c.target.name;
            }

            bool hostile = Combat.HostileNpcs.TurnedHostile(c);
            bool byHost = Combat.NetEnemies.Eligible(c);
            bool frozen = Combat.NetEnemies.Frozen.Contains(c);
            SaveableObject saveable = c.GetComponent<SaveableObject>();
            int id = 0;
            if (saveable != null)
            {
                id = saveable.uniqueId;
            }

            return head +
                $"target {target}, hostile {hostile}, run by host {byHost}, " +
                $"frozen {frozen}, save id {id}";
        }

        /// <summary>
        /// "npc-spawn NAME DX DZ" (host): the NPC prefab NAME, at the centre
        /// plus (DX, DZ), saved with the world.
        /// </summary>
        private static IEnumerator Spawn(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Core.AddPrefab(
                "Characters/NPC/" + a[0], at, Quaternion.Euler(90f, 0f, 0f),
                null, worldSpace: true);
            if (go == null)
            {
                r.Fail($"no prefab Characters/NPC/{a[0]}");
                yield break;
            }

            Core.addToSaveable(go, isDynamic: true, assignID: true);
            var c = go.GetComponent<Character>();
            if (c != null)
            {
                c.isActive = true;
            }

            yield return new WaitForSecondsRealtime(1f);
            if (c == null || c.npc == null)
            {
                r.Fail($"{a[0]} isn't an NPC");
                yield break;
            }

            r.Info = Props(c);
            if (Combat.HostileNpcs.TurnedHostile(c))
            {
                r.Fail($"it's hostile from the start: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-friendly NAME": this game has the NPC, friendly and run by
        /// itself.
        /// </summary>
        private static IEnumerator Friendly(string[] a, Result r)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            r.Info = Props(c);
            if (Combat.NetEnemies.Eligible(c)
                || Combat.NetEnemies.Frozen.Contains(c))
            {
                r.Fail($"it isn't ours and friendly: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-hit NAME DAMAGE [TIMES]": our player walks up to the NPC and
        /// hits it. Then: a client's copy is the host's (shown as its enemy),
        /// the host runs it. Info: its health before and after.
        /// </summary>
        private static IEnumerator Hit(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            float dmg = F(a[1]);
            int times = 1;
            if (a.Length > 2)
            {
                times = int.Parse(a[2]);
            }

            float was = c.health;
            _healthAtHit = P.health;
            for (int i = 0; i < times && c != null && c.alive; i++)
            {
                yield return Walk(c.transform.position, 100f);
                AsPlayer(
                    () => c.getHit(
                        dmg, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSecondsRealtime(0.6f);
            }

            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until
                && !(c != null && ne != null && ne.NetIdOf(c, out _)
                    && Combat.NetEnemies.Eligible(c)))
            {
                yield return null;
            }

            // the host's health for it, from its next states
            yield return new WaitForSecondsRealtime(1f);
            if (c == null)
            {
                r.Fail("it's gone");
                yield break;
            }

            r.Info = $"health {was:0} -> {c.health:0}; {Props(c)}";
            if (ne == null || !ne.NetIdOf(c, out _))
            {
                r.Fail($"it isn't one of the host's enemies: {r.Info}");
            }
            else if (c.health >= was)
            {
                r.Fail($"the hit didn't take health: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-host-runs NAME WHO" (host): we run the NPC as an enemy, and
        /// it's after WHO ("client" or "host").
        /// </summary>
        private static IEnumerator HostRuns(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            bool wantClient = a.Length < 2 || a[1] == "client";
            float until = Time.realtimeSinceStartup + 10f;
            bool ok = false;
            while (Time.realtimeSinceStartup < until && c != null)
            {
                Combat.NetPlayerProxy proxy = null;
                if (c.target != null)
                {
                    proxy = c.target.GetComponent<Combat.NetPlayerProxy>();
                }

                bool tracked = Combat.NetEnemies.Instance != null
                    && Combat.NetEnemies.Instance.IdOf(c, out _);
                if (!tracked)
                {
                    ok = false;
                }
                else if (wantClient)
                {
                    ok = proxy != null;
                }
                else
                {
                    ok = c.target != null
                        && c.target.GetComponent<Player>() != null;
                }

                if (ok)
                {
                    break;
                }

                yield return null;
            }

            if (c == null)
            {
                r.Fail("it's gone");
                yield break;
            }

            // One that lost sight of them since (an NPC that doesn't fight on)
            // was still sent after them by us.
            string sent = MapScenarioIssues.LogLines().LastOrDefault(
                l => l.Contains($"[npc] {c.name} sent after ")
                    || l.Contains($"[npc] {c.name} turned hostile"));
            bool tracked2 = Combat.NetEnemies.Instance != null
                && Combat.NetEnemies.Instance.IdOf(c, out _);
            if (!ok && wantClient && tracked2 && sent != null
                && !sent.Contains("the host's player"))
            {
                ok = true;
            }

            string details = Props(c);
            string line;
            if (sent != null)
            {
                line = sent.Trim();
            }
            else
            {
                line = "never sent after anyone";
            }

            r.Info = $"{details}; {line}";
            if (!ok)
            {
                string text = "host";
                if (wantClient)
                {
                    text = "client";
                }

                r.Fail($"not run by us after the {text}: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-attacks-us NAME": we stand by the NPC until it hurts us (its
        /// hits come from the host's copy).
        /// </summary>
        private static IEnumerator AttacksUs(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            float was;
            if (_healthAtHit > 0f)
            {
                was = _healthAtHit; // it may have hurt us since we hit it
            }
            else
            {
                was = P.health;
            }

            float until = Time.realtimeSinceStartup + 35f;
            while (Time.realtimeSinceStartup < until && P.health >= was
                && c != null && c.alive)
            {
                if (StateDump.Flat(c.transform.position
                        - P.transform.position) > 150f)
                {
                    yield return Walk(c.transform.position, 100f);
                }

                yield return null;
            }

            float health = P.health;
            string details = "gone";
            if (c != null)
            {
                details = Props(c);
            }

            r.Info = $"our health {was:0} -> {health:0}; {details}";
            if (P.health >= was)
            {
                r.Fail($"it never hurt us: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-provoke NAME": a scripted attack on our player by the NPC
        /// (GameEvent's attack step, as a dialogue's outcome runs it), in our
        /// game. A client's goes to the host, whose copy comes after us.
        /// </summary>
        private static IEnumerator Provoke(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            yield return Walk(c.transform.position, 300f);
            AsPlayer(() => c.attackPlayer());
            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until
                && !(c != null && ne != null && ne.NetIdOf(c, out _)))
            {
                yield return null;
            }

            if (c == null)
            {
                r.Fail("it's gone");
                yield break;
            }

            r.Info = Props(c);
            if (ne == null || !ne.NetIdOf(c, out _))
            {
                r.Fail($"it isn't one of the host's enemies: {r.Info}");
            }
        }

        /// <summary>
        /// "npc-kill NAME": our player hits the NPC until it's dead (a client's
        /// hits go to the host).
        /// </summary>
        private static IEnumerator Kill(string[] a, Result r)
        {
            var c = Npc(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            int hits = 0;
            while (c != null && c.alive && hits < 60)
            {
                yield return Walk(c.transform.position, 80f);
                hits++;
                AsPlayer(
                    () => c.getHit(
                        60f, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSecondsRealtime(0.4f);
            }

            // a client settles the corpse 2 s after it dies
            yield return new WaitForSecondsRealtime(3f);
            r.Info =
                $"{a[0]}: alive={(c != null && c.alive)} after {hits} hits, "
                    + $"searchable={(c != null && c.GetComponent<Item>() != null)}";
            if (c != null && c.alive)
            {
                r.Fail($"still alive: {r.Info}");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "npc-dead NAME": the NPC is dead in this game, and so is its story
        /// state.
        /// </summary>
        private static IEnumerator Dead(string[] a, Result r)
        {
            // A corpse may have lost its NPC part (the game's
            // destroyComponents): found by its kind, its story state by the
            // NPC's name ("npc-dead KIND NPCNAME").
            float until = Time.realtimeSinceStartup + 10f;
            Character c = null;
            Flags.NPCState st = null;
            while (Time.realtimeSinceStartup < until)
            {
                c = Object.FindObjectsOfType<Character>().Where(
                    x => x != null && Kind(x) == a[0])
                    .OrderBy(x => StateDump.Flat(x.transform.position
                            - Centre)).FirstOrDefault();
                if (Singleton<Flags>.Instance != null && a.Length > 1)
                {
                    st = Singleton<Flags>.Instance.getNPCState(a[1]);
                }
                else
                {
                    st = null;
                }

                if (c != null && !c.alive && (st == null || st.dead))
                {
                    break;
                }

                yield return null;
            }

            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            string deadText = "no state";
            if (st != null)
            {
                deadText = st.dead.ToString();
            }

            string part;
            if (a.Length > 2)
            {
                part = a[2];
            }
            else if (a.Length > 1)
            {
                part = a[1];
            }
            else
            {
                part = a[0];
            }

            r.Info =
                $"alive {c.alive}, story dead {deadText}, searchable "
                    + $"{c.GetComponent<Item>() != null}; flags "
                    + $"{FlagsWith(part)}";
            if (c.alive || (st != null && !st.dead))
            {
                r.Fail($"not dead here: {r.Info}");
            }
        }
    }
}
