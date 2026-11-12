using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// NPCs that turn on a player (gap 33). A friendly NPC is each player's own, as the rest of an NPC's story is.
    /// Once the host's copy turns hostile (a player hit it, a dialogue's outcome or a scripted attack sent it after
    /// one), the host runs it as one of its enemies (NetEnemies): its AI picks a player by threat over every player,
    /// the clients' copies are frozen and shown as the host's (found by save id, or for one a scripted event made,
    /// the one of that kind near the host's), hits on it from the clients are requests capped by their weapon, and
    /// its health, state and death are the host's. When it calms down (Character.calmDown: neutral and idle) for
    /// CalmTime, the host stops running it and each client's copy is its own again.
    ///
    /// A client's player turning a friendly NPC hostile doesn't do it in its own game: the hit goes to the host as
    /// NpcHit, a scripted "attack the player" as NpcAttack, and the host's copy turns on that player's stand-in.
    /// The host refuses one it hasn't got (an NPC only that client's game has, a hideout's own wolf or trader):
    /// that NPC is the client's, and the hit or attack happens there, as before.
    /// </summary>
    public static class HostileNpcs
    {
        /// <summary>How long a host-run NPC stays calm before it's each player's own again.</summary>
        public const float CalmTime = 8f;
        /// <summary>The host's refusal for an NPC it hasn't got (the client then acts on its own copy).</summary>
        public const string NotHere = "no such NPC here";

        // Host: the NPCs it runs as enemies. Client: its copies of those.
        private static readonly HashSet<Character> _running = new HashSet<Character>();

        /// <summary>Set while a client applies its own hit or attack to an NPC the host hasn't got.</summary>
        private static bool _ours;

        public static void Clear() => _running.Clear();

        /// <summary>
        /// Whether the host runs this NPC as an enemy: on the host, one it runs or one that just turned on a player;
        /// on a client, a copy of one of those, or one of ours that turned hostile by itself (frozen and hidden until
        /// the host's shows, as our own enemies are).
        /// </summary>
        public static bool Running(Character c) => c != null && c.npc != null && (_running.Contains(c) || TurnedHostile(c));

        /// <summary>The NPC is after a player, or will attack one on sight.</summary>
        public static bool TurnedHostile(Character c)
        {
            if (c == null || c.npc == null || !NetEnemies.InPlay(c)) return false;
            if (c.behaviour == Character.Behaviour.chasingTarget && IsPlayer(c.target)) return true;
            return c.aggressiveness == Aggressiveness.attackOnSight && c.attacksFaction(Faction.player);
        }

        /// <summary>Host: still not calmed down (once it attacks on sight it stays so until Character.calmDown).</summary>
        public static bool StillHostile(Character c) =>
            TurnedHostile(c) || c.aggressiveness == Aggressiveness.attackOnSight || c.behaviour == Character.Behaviour.chasingTarget;

        private static bool IsPlayer(Transform t) => t != null && (t.GetComponent<Player>() != null || t.GetComponent<NetPlayerProxy>() != null);

        internal static void HostStarted(Character c, uint id)
        {
            _running.Add(c);
            Plugin.Log.LogInfo($"[npc] {c.name} turned hostile ({c.aggressiveness}, {c.behaviour}, after {Who(c.target)}): the host runs it as enemy #{id}");
        }

        internal static void HostStopped(Character c, uint id)
        {
            _running.Remove(c);
            Plugin.Log.LogInfo($"[npc] {c.name} (#{id}) calmed down: each player's own again");
        }

        internal static void ClientStarted(Character c, uint id)
        {
            _running.Add(c);
            Plugin.Log.LogInfo($"[npc] {c.name} is hostile in the host's game: shown as its enemy #{id}");
        }

        /// <summary>Client: the host stopped sending this NPC. Ours from the save runs again here; true if it was handled.</summary>
        internal static bool ClientStopped(Character c, uint id, bool ours)
        {
            _running.Remove(c);
            if (ours) return false; // made from its prefab: it goes
            NetEnemies.Thaw(c);
            Plugin.Log.LogInfo($"[npc] the host stopped sending {c.name} (#{id}): it's ours to run again");
            return true;
        }

        private static string Who(Transform t)
        {
            if (t == null) return "nobody";
            var proxy = t.GetComponent<NetPlayerProxy>();
            return proxy != null ? proxy.OwnerName : t.GetComponent<Player>() != null ? "the host's player" : t.name;
        }

        /// <summary>
        /// Client: our copy of the host's NPC of this kind when its save id didn't name it (one a scripted event made
        /// in each game): the nearest one of ours within 3000 that isn't driven yet.
        /// </summary>
        internal static Character OurCopy(string kind, Vector3 pos, Func<Character, bool> driven, Func<Character, string> kindOf)
        {
            Character best = null; float bestD = 3000f * 3000f;
            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c == null || c.npc == null || driven(c) || kindOf(c) != kind) continue;
                float d = (c.transform.position - pos).sqrMagnitude;
                if (d < bestD) { best = c; bestD = d; }
            }
            if (best != null) Plugin.Log.LogInfo($"[npc] the host's {kind} is our {best.name} near it");
            return best;
        }

        // ---- client: our player's hit or attack goes to the host ------------------------------------------

        /// <summary>
        /// Client, from ClientForwardHitPatch: a hit on an NPC of ours that the host doesn't run yet. True if it's
        /// handled (sent, or left to the host's copy): don't apply it here.
        /// </summary>
        internal static bool ClientHit(Character c, float damage, Transform attacker, bool cut, bool byPlayer, bool interrupt, bool normal)
        {
            if (_ours || c == null || c.npc == null) return false;
            if (!c.alive || c.isNightTrader || NetEnemies.LocalOnly(c))
            {
                Plugin.Log.LogInfo($"[npc] our hit on {c.name} is ours (alive {c.alive}, night trader {c.isNightTrader}, local {NetEnemies.LocalOnly(c)})");
                return false;
            }
            // Something thrown hits the NPC in the host's copy of the throw.
            if (ThrownHits.Now()) return true;
            var flame = ThrownHits.CurrentFlame;
            if (flame != null && !EnemyAreaHits.ByLocalPlayer(flame)) return false;
            bool fromPlayer = byPlayer || (attacker != null && attacker.GetComponent<Player>() != null);
            if (!fromPlayer) return false; // what the world does to it happens in each game
            var effects = NetEnemies.HitEffects;
            if (damage <= 0f && (effects == null || effects.Count == 0)) return false;
            var s = Session.Instance;
            var w = s.Begin(MsgType.NpcHit);
            ObjRef.Of(c.gameObject).Write(w);
            w.W.Write(damage);
            w.W.Write((byte)((cut ? 1 : 0) | 2 | (interrupt ? 4 : 0) | (normal ? 8 : 0)));
            PlayerDamage.WriteEffects(w, effects);
            var item = Player.Instance != null ? Player.Instance.currentItem : null;
            w.WriteStr(InvItemClass.isNull(item) ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(new InvItemClass.SaveState(item, 0)));
            var mine = effects != null ? new List<InvItemEffect>(effects) : null;
            Plugin.Log.LogInfo($"[npc] our hit on {c.name} for {damage:0} goes to the host");
            Authority.Request(w, $"hit on {c.name} for {damage:0}", (ok, reason, result) =>
            {
                if (ok || reason != NotHere || c == null || !c.alive || Player.Instance == null) return;
                // Only our game has it: our hit, here.
                _ours = true;
                try
                {
                    c.getHit(damage, Player.Instance.transform, cut, true, interrupt, normal, false, false, false);
                    if (mine != null && c.effects != null) foreach (var e in mine) c.effects.activate(e);
                }
                finally { _ours = false; }
            }, inventory: false);
            Core.AddPrefab("FX/Bloodsplats/Shotsplat_stay", c.transform.position, Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f), null);
            return true;
        }

        /// <summary>Client: a scripted "attack the player" on an NPC of ours. True if it isn't to be done here.</summary>
        internal static bool ClientAttack(Character c)
        {
            if (_ours || c == null || c.npc == null || c.dummy || !NetEnemies.ClientMode || NetEnemies.LocalOnly(c)) return false;
            var ne = NetEnemies.Instance;
            if (ne != null && ne.IsPuppet(c, out _)) return true; // the host's: it's already hostile there
            if (NetEnemies.Frozen.Contains(c)) return true;
            // Another player's event, run here too: the host sent its copy after them already.
            if (WorldActions.Replaying || ScriptedEvents.Current == ScriptedEvents.Part.SharedOnly) return true;
            var s = Session.Instance;
            var w = s.Begin(MsgType.NpcAttack);
            ObjRef.Of(c.gameObject).Write(w);
            Authority.Request(w, $"{c.name} attacks us", (ok, reason, result) =>
            {
                if (ok || reason != NotHere || c == null || !c.alive) return;
                _ours = true;
                try { c.attackPlayer(); }
                finally { _ours = false; }
            }, inventory: false);
            return true;
        }

        // ---- host --------------------------------------------------------------------------------------------

        private static Character Resolve(ulong from, ObjRef obj, out NetPlayerProxy proxy)
        {
            proxy = null;
            var c = ObjRef.Resolve<Character>(obj);
            if (c == null || c.npc == null) { Authority.Refuse(NotHere); return null; }
            if (!NetEnemies.InPlay(c)) { Authority.Refuse($"{c.name} isn't alive here"); return null; }
            if (!NetPlayerProxy.All.TryGetValue(from, out proxy) || proxy == null) { Authority.Refuse("no stand-in for that player yet"); return null; }
            return c;
        }

        internal static void OnNpcHit(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost) return;
            var obj = ObjRef.Read(r);
            float damage = r.R.ReadSingle();
            byte f = r.R.ReadByte();
            var effects = PlayerDamage.ReadEffects(r);
            string weapon = r.ReadStr();
            var c = Resolve(from, obj, out var proxy);
            if (c == null) return;
            Plugin.Log.LogInfo($"[npc] {s.NameOf(from)} hit {c.name} for {damage:0}");
            NetEnemies.Instance?.ApplyHit(from, proxy, c, damage, f, effects, weapon);
        }

        internal static void OnNpcAttack(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost) return;
            var obj = ObjRef.Read(r);
            var c = Resolve(from, obj, out var proxy);
            if (c == null) return;
            if (!Authority.InReach(c.transform.position, NetEnemies.RelevantRange, c.name)) return;
            SendAfter(c, proxy);
        }

        /// <summary>Host: send this NPC after a player's stand-in, as Character.attackPlayer does for our player.</summary>
        internal static void SendAfter(Character c, NetPlayerProxy proxy)
        {
            if (c.aggressiveness != Aggressiveness.defensive) c.aggressiveness = Aggressiveness.attackOnSight;
            c.attackCharacter(proxy.transform);
            Plugin.Log.LogInfo($"[npc] {c.name} sent after {proxy.OwnerName}");
        }
    }

    /// <summary>Client: a scripted attack by one of our NPCs is the host's to make (HostileNpcs.ClientAttack).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.attackPlayer))]
    static class ClientNpcAttackPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Character __instance) => !HostileNpcs.ClientAttack(__instance);
    }
}
