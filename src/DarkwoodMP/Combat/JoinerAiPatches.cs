using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: places in the game's enemy code that only react to the host's own player (a Player component, or
    /// Player.Instance), made to react to a joiner's proxy the same way. With nobody else in the world every
    /// helper here gives what the game's own code gives, so single player is unchanged.
    /// </summary>
    static class JoinerAi
    {
        /// <summary>
        /// Swaps each run of instructions that <paramref name="match"/> recognises (it returns the run's length, or 0)
        /// for "ldarg.0; call helper", padded with nops, keeping the labels. Logs a warning when nothing matched
        /// (the game's code changed), and the method then keeps the game's own behaviour.
        /// </summary>
        public static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, Func<List<CodeInstruction>, int, int> match,
                                                        MethodInfo helper, string where, int expected = 1)
        {
            var list = new List<CodeInstruction>(instructions);
            int swapped = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int n = match(list, i);
                if (n <= 0) continue;
                list[i] = new CodeInstruction(OpCodes.Ldarg_0) { labels = list[i].labels, blocks = list[i].blocks };
                list[i + 1] = new CodeInstruction(OpCodes.Call, helper) { labels = list[i + 1].labels, blocks = list[i + 1].blocks };
                for (int k = 2; k < n; k++) list[i + k] = new CodeInstruction(OpCodes.Nop) { labels = list[i + k].labels, blocks = list[i + k].blocks };
                swapped++;
                i += n - 1;
            }
            if (swapped != expected)
                Plugin.Log.LogWarning($"JoinerAi: {where}: swapped {swapped} reads of the host's player, expected {expected} (game changed?)");
            return list;
        }

        public static readonly MethodInfo GetInstance = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Instance));

        /// <summary>Player.Instance.whereAmI.bigLocation</summary>
        public static int HostBigLocation(List<CodeInstruction> l, int i)
        {
            var whereAmI = AccessTools.Field(typeof(Player), nameof(Player.whereAmI));
            var big = AccessTools.PropertyGetter(typeof(WhereAmI), nameof(WhereAmI.bigLocation));
            return i + 2 < l.Count && l[i].Calls(GetInstance) && l[i + 1].LoadsField(whereAmI) && l[i + 2].Calls(big) ? 3 : 0;
        }

        /// <summary>Player.Instance.&lt;getter&gt;</summary>
        public static Func<List<CodeInstruction>, int, int> HostThen(MethodInfo getter) =>
            (l, i) => i + 1 < l.Count && l[i].Calls(GetInstance) && l[i + 1].Calls(getter) ? 2 : 0;

        /// <summary>Player.Instance.&lt;field&gt;</summary>
        public static Func<List<CodeInstruction>, int, int> HostField(FieldInfo field) =>
            (l, i) => i + 1 < l.Count && l[i].Calls(GetInstance) && l[i + 1].LoadsField(field) ? 2 : 0;

        public static NetPlayerProxy ProxyOf(Transform t) => t != null && Threat.Enabled ? t.GetComponent<NetPlayerProxy>() : null;

        private static readonly RaycastHit[] _hit = new RaycastHit[1];

        /// <summary>Whether a position is on indoor ground, the way CharBase.checkGround decides isInside.</summary>
        public static bool InsideAt(Vector3 pos)
        {
            if (!Physics.Raycast(pos + new Vector3(0f, 300f, 0f), Vector3.down, out var hit, 1000f, 2)) return false;
            var g = hit.collider != null ? hit.collider.GetComponent<Ground>() : null;
            return g != null && g.isInside;
        }

        /// <summary>
        /// Part of "--dwmp-test-night" (after the night attacker): on character c and a proxy, checks the patches
        /// here by calling the game's methods as it would. Logs "[joiner ai test]" lines.
        /// </summary>
        public static void Test(Character c, NetPlayerProxy proxy)
        {
            if (c == null || proxy == null) { Plugin.Log.LogInfo("[joiner ai test] nothing to test on"); return; }
            string T(Transform t) => t == null ? "nobody" : t == proxy.transform ? proxy.OwnerName : t.name;
            try
            {
                foreach (var a in c.activities)
                {
                    if (a == null || !(a.getFromLocation || a.playerIsTarget)) continue;
                    a.thisCharacter = c;
                    a.assignTarget();
                    var loc = a.target != null ? NightSpawns.BigLocationAt(a.target.transform.position) : null;
                    Plugin.Log.LogInfo($"[joiner ai test] activity {a.type}: target {(a.target != null ? a.target.name : "none")}, in {(loc != null ? loc.name : "no location")}");
                }
                Plugin.Log.LogInfo($"[joiner ai test] {c.name} has {c.activities.Count} activities");

                c.transform.position = proxy.transform.position + new Vector3(800f, 0f, 0f);
                bool near = c.inSightOrCloseToPlayer();
                Plugin.Log.LogInfo($"[joiner ai test] near the joiner: {near}, {Core.trueDistance(c.transform.position, Player.Instance.transform.position):0} from the host");

                var onCollide = AccessTools.Method(typeof(Character), "onCollideWith");
                c.sleeping = true; c.wakingUp = false;
                onCollide.Invoke(c, new object[] { proxy.GetComponent<Collider>() });
                Plugin.Log.LogInfo($"[joiner ai test] bump on a sleeping {c.name}: waking up {c.wakingUp}");
                c.sleeping = false; c.wakingUp = false;

                c.stopAttacking(proxy.transform);
                c.cowardiness = Cowardiness.always;
                if (c.aggressiveness == Aggressiveness.flee || c.aggressiveness == Aggressiveness.fleeAndDespawn) c.aggressiveness = Aggressiveness.attackOnSight;
                AccessTools.Method(typeof(Character), "reactToCharacter").Invoke(c, new object[] { null, proxy.transform, true });
                Plugin.Log.LogInfo($"[joiner ai test] hit by the joiner (cowardiness always): behaviour {c.behaviour}, target {T(c.target)}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[joiner ai test] failed: {e}"); }
        }

        // ---- helpers the transpilers call ----

        /// <summary>The forest spirit leaves when its player goes indoors: the joiner it chases, or else the host's.</summary>
        public static bool TargetIsInside(Character c)
        {
            var proxy = ProxyOf(c != null ? c.target : null);
            if (proxy != null) return InsideAt(proxy.transform.position);
            return Player.Instance.isInside;
        }

        /// <summary>reactToCharacter's "is the attacker the player": the host's player, or a joiner's proxy.</summary>
        public static Component PlayerOrProxyOn(Transform t)
        {
            if (t == null) return null;
            var p = t.GetComponent<Player>();
            if (p != null) return p;
            return ProxyOf(t);
        }

        /// <summary>reactToCharacter's attackPlayer(): a joiner who hit or bumped the enemy is the one it goes for.</summary>
        public static void AttackWhoReacted(Character c, Transform attacker)
        {
            var proxy = ProxyOf(attacker);
            if (proxy == null) { c.attackPlayer(); return; }
            // An NPC turns on the joiner here, and the host runs it as an enemy from then on (Combat.HostileNpcs).
            if (c.aggressiveness != Aggressiveness.defensive) c.aggressiveness = Aggressiveness.attackOnSight;
            c.attackCharacter(proxy.transform);
            if (Threat.Debug) Plugin.Log.LogInfo($"[threat] {c.name} reacts to {proxy.OwnerName}: goes for them");
        }

        /// <summary>A hatching spider's location: the host's, unless a joiner is the nearest player; then where it is.</summary>
        public static Location HatchLocation(Character c)
        {
            var host = Player.Instance;
            var hostLoc = host.whereAmI != null ? host.whereAmI.bigLocation : null;
            if (!Threat.Enabled || c == null) return hostLoc;
            return Threat.NearestPlayerPos(c) == host.transform.position ? hostLoc : null; // null: the game uses transform.GetLocation()
        }

        /// <summary>
        /// A spawned character's activity (getFromLocation) takes its target from "the player's" location: for one
        /// sent at a joiner's hideout that hideout, for others the location of the player it is after.
        /// </summary>
        public static Location SpawnedLocation(Character.Activity a)
        {
            var host = Player.Instance;
            var hostLoc = host.whereAmI != null ? host.whereAmI.bigLocation : null;
            var c = a?.thisCharacter;
            if (!Threat.Enabled || c == null) return hostLoc;
            if (NightSpawns.SentAt(c, out var proxy, out var loc) && loc != null) return loc;
            if (proxy == null) proxy = Threat.BestPlayerFor(c) as NetPlayerProxy;
            if (proxy == null) return hostLoc;
            return NightSpawns.BigLocationAt(proxy.transform.position) ?? hostLoc;
        }

        /// <summary>An activity aimed at "the player" (playerIsTarget): the joiner it was sent at, or the most-threat player.</summary>
        public static GameObject ActivityPlayer(Character.Activity a)
        {
            var c = a?.thisCharacter;
            if (!Threat.Enabled || c == null) return Player.Instance.gameObject;
            if (NightSpawns.SentAt(c, out var proxy, out _) && proxy != null && proxy.alive) return proxy.gameObject;
            var best = Threat.BestPlayerFor(c);
            return best != null ? best.gameObject : Player.Instance.gameObject;
        }
    }

    /// <summary>
    /// Host: an enemy bumping into a joiner's proxy does what it does when it bumps into the host's player
    /// (Character.onCollideWith only checks for a Player): a banshee goes off, a sleeping enemy wakes, others react
    /// to them. The rest of the game's method (doors, water and so on) still runs, as it does after a player bump.
    /// </summary>
    [HarmonyPatch(typeof(Character), "onCollideWith")]
    static class ProxyBumpPatch
    {
        private static readonly Action<Character, Character, Transform, bool> React =
            AccessTools.MethodDelegate<Action<Character, Character, Transform, bool>>(AccessTools.Method(typeof(Character), "reactToCharacter"));

        static bool Prefix(Character __instance, Collider _collider)
        {
            if (_collider == null || !Threat.Enabled || NetEnemies.ClientMode || !__instance.isActive) return true;
            var proxy = _collider.GetComponent<NetPlayerProxy>();
            if (proxy == null || !proxy.alive || proxy.Away) return true;
            var c = __instance;
            if (!c.touchingColliders.Contains(_collider)) c.touchingColliders.Add(_collider);
            if (c.banshee)
            {
                if (Threat.Debug) Plugin.Log.LogInfo($"[threat] {c.name} (banshee) touched by {proxy.OwnerName}");
                c.initiateBansheeAttack();
                return false;
            }
            if (c.sleeping && !c.wakeUpOnlyManually)
            {
                if (Threat.Debug) Plugin.Log.LogInfo($"[threat] {c.name} woken by {proxy.OwnerName} bumping into it");
                c.wakeup();
                return false;
            }
            if (!proxy.invisible && !c.dieOnContactWithTarget) React(c, null, proxy.transform, false);
            return true;
        }
    }

    /// <summary>
    /// Host: an enemy hit or bumped by a joiner reacts as it does to the host's player: it chases and attacks them,
    /// instead of rolling its cowardice. The transpiler swaps reactToCharacter's GetComponent&lt;Player&gt;() test for
    /// one that also accepts a proxy, and its attackPlayer() for an attack on whoever it reacted to.
    /// </summary>
    [HarmonyPatch(typeof(Character), "reactToCharacter")]
    static class ReactToJoinerPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var attackPlayer = AccessTools.Method(typeof(Character), nameof(Character.attackPlayer));
            var list = new List<CodeInstruction>(instructions);
            int test = list.FindIndex(ci => ci.operand is MethodInfo m && m.Name == "GetComponent" && m.IsGenericMethod
                                            && m.GetGenericArguments()[0] == typeof(Player));
            int call = test < 0 ? -1 : list.FindIndex(test, ci => ci.Calls(attackPlayer));
            if (test < 0 || call < 0)
            {
                Plugin.Log.LogWarning("ReactToJoinerPatch: no GetComponent<Player>() / attackPlayer() in reactToCharacter (game changed?); a joiner's hit still rolls cowardice");
                return list;
            }
            list[test] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.PlayerOrProxyOn))) { labels = list[test].labels };
            var labels = list[call].labels;
            list[call] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.AttackWhoReacted)));
            list.Insert(call, new CodeInstruction(OpCodes.Ldarg_2) { labels = labels });
            return list;
        }
    }

    /// <summary>
    /// Host: an enemy that left its location doesn't jump back to its spawn point while a joiner is near it
    /// (inSightOrCloseToPlayer only looks at the host's player). Line of sight to a proxy isn't checked, so a
    /// joiner counts within 1500, more than the game's 1000 for the host's player.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.inSightOrCloseToPlayer))]
    static class NearJoinerPatch
    {
        static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || !Threat.Enabled) return;
            Vector3 me = __instance.transform.position;
            foreach (var p in NetPlayerProxy.All.Values)
                if (p != null && p.alive && !p.Away && Core.trueDistance(p.transform.position, me) < 1500f) { __result = true; return; }
        }
    }

    /// <summary>Host: the forest spirit leaves when the player it chases goes indoors, not whenever the host does.</summary>
    [HarmonyPatch(typeof(Character), "checkStuff")]
    static class ForestSpiritIndoorsPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(instructions, JoinerAi.HostField(AccessTools.Field(typeof(CharBase), nameof(CharBase.isInside))),
                          AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.TargetIsInside)), "Character.checkStuff (forest spirit)");
    }

    /// <summary>Host: a defensive enemy that wakes up paths toward the player it has the most threat for, not always the host.</summary>
    [HarmonyPatch(typeof(Character), "OnAniFinish")]
    static class DefensiveWakeTargetPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(instructions, JoinerAi.HostThen(AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform))),
                          AccessTools.Method(typeof(Threat), nameof(Threat.PursuitTarget)), "Character.OnAniFinish");
    }

    /// <summary>Host: a hatching spider joins the location it is in when a joiner, not the host, is the nearest player.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.onSpiderSpawn))]
    static class SpiderHatchLocationPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            JoinerAi.Swap(instructions, JoinerAi.HostBigLocation, AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.HatchLocation)), "Character.onSpiderSpawn");
    }

    /// <summary>
    /// Host: a spawned character's activities (going to a location's doors, windows and so on, or to "the player")
    /// use the hideout of the joiner it was sent at, or the location of the player it's after, not the host's.
    /// </summary>
    [HarmonyPatch(typeof(Character.Activity), nameof(Character.Activity.assignTarget))]
    static class SpawnedActivityTargetPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var l = JoinerAi.Swap(instructions, JoinerAi.HostBigLocation, AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.SpawnedLocation)),
                                  "Character.Activity.assignTarget (location)", expected: 2);
            return JoinerAi.Swap(l, JoinerAi.HostThen(AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject))),
                                 AccessTools.Method(typeof(JoinerAi), nameof(JoinerAi.ActivityPlayer)), "Character.Activity.assignTarget (player)");
        }
    }

    /// <summary>
    /// Host: an enemy chasing a joiner into a location starts at the waypoint nearest them, as checkOutLocation does
    /// for the host's player when it's inside. The prefix notes the joiner; gotoNextWaypoint then gets the waypoint.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.checkOutLocation))]
    static class CheckOutLocationJoinerPatch
    {
        [ThreadStatic] public static NetPlayerProxy Chased;

        static void Prefix(Character __instance)
        {
            Chased = null;
            if (!Threat.Enabled || __instance.checkingOutLocation) return;
            var proxy = JoinerAi.ProxyOf(__instance.superTarget);
            if (proxy != null && proxy.alive && JoinerAi.InsideAt(proxy.transform.position)) Chased = proxy;
        }

        static Exception Finalizer(Exception __exception) { Chased = null; return __exception; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.gotoNextWaypoint))]
    static class CheckOutLocationWaypointPatch
    {
        static void Prefix(Character __instance, ref Waypoint _waypoint)
        {
            var proxy = CheckOutLocationJoinerPatch.Chased;
            if (proxy == null || _waypoint != null) return;
            CheckOutLocationJoinerPatch.Chased = null; // only the call checkOutLocation makes
            var loc = __instance.locationCheckingOut;
            if (loc == null) return;
            Waypoint best = null; float bestDist = float.MaxValue;
            Vector3 at = proxy.transform.position;
            foreach (var w in loc.waypointsInside)
            {
                if (w == null) continue;
                float d = (w.transform.position - at).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = w; }
            }
            _waypoint = best;
        }
    }

    /// <summary>Host: the scripted brother's weak attack goes by the health of the player it is fighting.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.checkBrotherWeakAttack))]
    static class BrotherWeakAttackPatch
    {
        static bool Prefix(Character __instance)
        {
            var proxy = JoinerAi.ProxyOf(__instance.target);
            if (proxy == null) return true;
            float max = Player.Instance != null ? Player.Instance.maxHealth : 100f;
            if (proxy.HealthPercent / 100f * max < 20f && __instance.attacks.Count >= 3)
            {
                __instance.attacks[0].disabled = true;
                __instance.attacks[1].disabled = true;
                __instance.attacks[2].disabled = false;
            }
            return false;
        }
    }
}
