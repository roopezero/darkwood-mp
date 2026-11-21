using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Sync.Players;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps that fight each kind of enemy and creature in the game
    /// (tools/tests/map_scenario_enemies.py): the host spawns one beside the
    /// client's player and watches whom it goes for and what it spawns; the
    /// client waits for it to come and hurt it, then hits it (and whatever it
    /// spawned) with the player's own melee until it's dead, and searches the
    /// corpse. Both games' "enemies" dumps are compared after each.
    /// </summary>
    public static partial class MapScenarioEnemies
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("ee-arena", Arena, timeout: 30f);
            Add("ee-spawn", Spawn, timeout: 15f);
            Add("ee-fight", Fight, timeout: 60f);
            Add("ee-check", Check, timeout: 15f);
            Add("ee-corpse", Corpse, timeout: 20f);
            Add("ee-clear", Clear, timeout: 10f);
            Add("ee-solo", Solo, timeout: 25f);
            Add("ee-shadow-spawn", ShadowSpawn, timeout: 15f);
            Add("ee-flashlight", Flashlight, timeout: 20f);
            Add("ee-shadow-check", ShadowCheck, timeout: 10f);
            Add("ee-armor-check", ArmorCheck, timeout: 10f);
            Add("ee-armor-burn", ArmorBurn, timeout: 15f);
            Add("ee-flee-activity", FleeActivity, timeout: 15f);
            Add("ee-day", Day, timeout: 10f);
        }

        // ---- the clock ------------------------------------------------------

        private static bool _holdDay;
        /// <summary>
        /// The characters there were when the clock was first held: ee-clear
        /// leaves these alone.
        /// </summary>
        private static readonly HashSet<int> _baseline = new HashSet<int>();

        /// <summary>
        /// "ee-day [off]" (host): the clock set to noon and held there
        /// (Controller.DoUpdateTime off, put back if the game turns it on), so
        /// no night (its worms and attackers) comes during the fights; "off"
        /// lets it run again. The clients follow the host's clock (TimeSync).
        /// </summary>
        private static IEnumerator Day(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s != null && s.IsClient)
            {
                r.Fail("the clock is the host's");
                yield break;
            }

            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                r.Fail("no clock");
                yield break;
            }

            if (a.Length > 0 && a[0] == "off")
            {
                _holdDay = false;
                ctl.DoUpdateTime = true;
                r.Info =
                    $"the clock runs again from {ctl.CurrentTime / 60f:0.00} h";
                yield break;
            }

            TestWorld.SetTime(720f);
            if (_baseline.Count == 0)
            {
                foreach (var c in Object.FindObjectsOfType<Character>())
                {
                    _baseline.Add(c.GetInstanceID());
                }
            }

            if (!_holdDay)
            {
                _holdDay = true;
                Plugin.Instance.StartCoroutine(HoldDay());
            }

            yield return new WaitForSeconds(1.5f);
            r.Info = $"day {ctl.day} {ctl.CurrentTime / 60f:0.00} h, held";
        }

        private static IEnumerator HoldDay()
        {
            while (_holdDay)
            {
                var ctl = Singleton<Controller>.Instance;
                var s = Net.Session.Instance;
                if (ctl == null || s == null || !s.IsHost)
                {
                    _holdDay = false;
                    break;
                }

                if (ctl.DoUpdateTime)
                {
                    ctl.DoUpdateTime = false;
                }

                if (ctl.CurrentTime != 720)
                {
                    TestWorld.SetTime(720f);
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---- what the fights spawn ------------------------------------------
        // Logged, to find where a thing only one game has came from.

        /// <summary>
        /// While the fights run: things made by name (Core.AddPrefab) are
        /// logged with where they came from.
        /// </summary>
        internal static bool TraceSpawns;

        internal static void Spawned(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            string n = go.name;
            if (n.StartsWith("Fov_") || n.Contains("sound_loop")
                || n.Contains("Spawner"))
            {
                System.Diagnostics.StackFrame[] st =
                    new System.Diagnostics.StackTrace(2, false).GetFrames();
                if (st == null)
                {
                    st = new System.Diagnostics.StackFrame[0];
                }

                string from = string.Join(" < ", st.Take(8)
                        .Select(f => f.GetMethod()).Where(m => m != null)
                        .Select(m =>
                {
                    Type declaringType = m.DeclaringType;
                    string name = null;
                    if (declaringType != null)
                    {
                        name = declaringType.Name;
                    }

                    return name + "." + m.Name;
                }));

                string comps = string.Join(
                    "/",
                    go.GetComponentsInChildren<MonoBehaviour>(true)
                        .Where(m => m != null).Select(m => m.GetType().Name)
                        .Distinct());
                string place = Fmt(go.transform.position);
                string parent = "none";
                if (go.transform.parent != null)
                {
                    parent = go.transform.parent.name;
                }

                Plugin.Log.LogInfo(
                    $"[ee] made {n} at {place} (parent {parent}; {comps}) from "
                        + $"{from}");
            }
        }

        /// <summary>
        /// Where the fights are: away from the stations (the host stays at the
        /// centre, out of the way).
        /// </summary>
        private static Vector3 ArenaSpot =>
            FreeSpot(Centre + new Vector3(0f, 0f, 1500f));

        private static string Short(string kind)
        {
            if (kind.Contains('/'))
            {
                return kind.Substring(kind.LastIndexOf('/') + 1);
            }

            return kind;
        }

        private static bool IsKind(Character c, string kind) => c != null
            && c.name.Replace("(Clone)", "").Trim()
                .Equals(Short(kind), StringComparison.OrdinalIgnoreCase);

        internal static Vector3 FreeSpot(Vector3 at)
        {
            for (int ring = 0; ring < 12; ring++)
            {
                for (int i = 0; i < Math.Max(1, ring * 6); i++)
                {
                    float a = i * Mathf.PI * 2f / Math.Max(1, ring * 6);
                    var p = at
                        + new Vector3(Mathf.Cos(a), 0f, Mathf
                            .Sin(a)) * ring * 50f;
                    if (!Physics.OverlapSphere(
                        p, 60f, ~0, QueryTriggerInteraction.Ignore).Any(
                            c => c.attachedRigidbody == null
                                || !c.attachedRigidbody.CompareTag("Player")))
                    {
                        return p;
                    }
                }
            }

            return at;
        }

        /// <summary>The (one) remote player's stand-in on the host.</summary>
        internal static NetPlayerProxy ClientProxy() =>
            NetPlayerProxy.All.Values.FirstOrDefault(
                p => p != null && p.Owner != PlayerSync.DummyId);

        /// <summary>
        /// KIND spawned at the spot, sent after "the player" if attack (null:
        /// it couldn't be).
        /// </summary>
        private static Character SpawnAt(string kind, Vector3 spot, bool attack)
        {
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                P.gameObject, spot, 1f, kind, nocturnal: false,
                attackPlayer: attack);
            if (c != null)
            {
                c.transform.position = spot;
            }

            return c;
        }

        /// <summary>Kill it as our player's own hit.</summary>
        private static void Kill(Character c) => AsPlayer(
            () => c.getHit(c.health + 1000f, P.transform, false, true, false));

        // ---- P8: a scripted run-away activity -------------------------------

        /// <summary>
        /// "ee-flee-activity" (host): a Chicken beside the client, given the
        /// game's runAway activity with no target (as a scripted event does),
        /// runs from the client's player (the nearest), not from ours behind
        /// it.
        /// </summary>
        private static IEnumerator FleeActivity(string[] a, Result r)
        {
            var proxy = ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }

            var host = P.transform.position;
            var toClient = proxy.transform.position - host;
            toClient.y = 0f;
            // between the two players, nearer the client
            var spot =
                FreeSpot(proxy.transform.position - toClient.normalized * 250f);
            var c = SpawnAt("Chicken", spot, attack: false);
            if (c == null)
            {
                r.Fail("couldn't spawn a chicken");
                yield break;
            }

            yield return new WaitForSeconds(1f);
            var act = new Character.Activity
            {
                type = Character.Activity.Type.runAway
            };

            act.run(c);
            yield return new WaitForSeconds(3f);
            var now = c.transform.position;
            float fromClient =
                Core.trueDistance(now, proxy.transform.position)
                    - Core.trueDistance(spot, proxy.transform.position);
            float fromHost =
                Core.trueDistance(now, host) - Core.trueDistance(spot, host);
            r.Info =
                $"{c.name} from {Fmt(spot)} to {Fmt(now)}: {fromClient:0} "
                    + $"further from the client, {fromHost:0} further from us";
            Kill(c);
            yield return new WaitForSeconds(1f);
            if (c != null)
            {
                Object.Destroy(c.gameObject);
            }

            if (!(fromClient > 100f))
            {
                r.Fail($"it didn't run from the client: {r.Info}");
            }
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class EnemyTestSpawnTracePatch
    {
        static void Postfix(GameObject __result)
        {
            if (MapScenarioEnemies.TraceSpawns)
            {
                MapScenarioEnemies.Spawned(__result);
            }
        }
    }
}
