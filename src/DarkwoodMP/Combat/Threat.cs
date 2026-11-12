using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Threat-based targeting, on the host, while there are several players. Each enemy keeps a threat value
    /// for every player it knows of, instead of going for whoever its sight check happened to list last:
    ///
    ///   damage      every point of damage a player deals to it                         x1.0
    ///   noise       sounds a player makes that it hears: their footsteps, running and
    ///               shots (the game's Character.alertInArea, raised from Player code;
    ///               remote players' come from their games as PlayerNoise)               volume x NoiseWeight
    ///   being seen  every second it can see a player                                    SeenPerSecond
    ///   closeness   at choosing time, not stored: up to CloseWeight for a player
    ///               right next to it, nothing at the edge of its sight
    ///
    /// Stored threat fades (half-life HalfLife seconds), so an enemy loses interest in someone who stopped
    /// bothering it. It goes for the player with the most threat among those it can see, and only turns from its
    /// current target to another once that one has SwitchMargin times as much, so it doesn't flip back and forth.
    ///
    /// The game's own reactions stay: being hit still makes it react to the attacker at once, and the next
    /// sight check decides by threat who it keeps after. The choice is made by ordering the enemy's view-range
    /// list before its sight check (see BeforeSightCheck), so all of the game's own targeting follows it.
    /// </summary>
    public static class Threat
    {
        public const float NoiseWeight = 6f;
        public const float SeenPerSecond = 2f;
        public const float CloseWeight = 25f;
        /// <summary>The least distance over which closeness counts when choosing "the player" for an enemy (BestPlayerFor).</summary>
        public const float MinCloseRange = 1500f;
        public const float HalfLife = 15f;
        public const float SwitchMargin = 1.3f;
        /// <summary>Who's making the noise being broadcast right now (set around Character.alertInArea), or null.</summary>
        public static CharBase NoiseSource;

        public sealed class Table
        {
            public readonly Dictionary<CharBase, float> Values = new Dictionary<CharBase, float>();
            public float LastDecay = Time.time;
            public float LastSeenTick = Time.time;

            public void Decay()
            {
                float dt = Time.time - LastDecay;
                if (dt <= 0f) return;
                LastDecay = Time.time;
                float k = Mathf.Pow(0.5f, dt / HalfLife);
                var keys = new List<CharBase>(Values.Keys);
                foreach (var c in keys)
                {
                    if (c == null) { Values.Remove(c); continue; }
                    float v = Values[c] * k;
                    if (v < 0.05f) Values.Remove(c); else Values[c] = v;
                }
            }

            public void Add(CharBase who, float amount)
            {
                if (who == null || amount <= 0f) return;
                Decay();
                Values.TryGetValue(who, out float v);
                Values[who] = v + amount;
            }

            public float Get(CharBase who) => who != null && Values.TryGetValue(who, out float v) ? v : 0f;
        }

        private static readonly ConditionalWeakTable<Character, Table> _tables = new ConditionalWeakTable<Character, Table>();

        public static Table Of(Character c) => _tables.GetValue(c, _ => new Table());

        /// <summary>Only while the host has other players; alone, the game's own AI is left exactly as it was.</summary>
        public static bool Enabled => Proxies.Any;
        /// <summary>Log every target change with its scores ("--dwmp-debug-threat").</summary>
        public static bool Debug;

        // ---- sources -------------------------------------------------------

        public static void OnDamaged(Character victim, CharBase attacker, float damage)
        {
            if (!Enabled || !Proxies.IsPlayer(attacker)) return;
            Of(victim).Add(attacker, damage);
        }

        public static void OnHeard(Character listener, Vector3 position, float volume)
        {
            if (!Enabled || NoiseSource == null) return; // only noise a player made is theirs
            Of(listener).Add(NoiseSource, Mathf.Max(0.1f, volume) * NoiseWeight);
            if (Debug) Plugin.Log.LogInfo($"[threat] {listener.name} heard {Name(NoiseSource)} (+{Mathf.Max(0.1f, volume) * NoiseWeight:0.0}) at {position}");
        }

        /// <summary>
        /// Before the game's sight check. The game targets whichever visible character comes last in its view-range
        /// list, and acts on it within the same check (stops to listen, chases, attacks), so the choice has to be
        /// made before it runs: the players in the list are put at its end, least threat first, so the one with the
        /// most threat is the one the game picks, if it can see them. A player it's already after keeps that place
        /// unless another has SwitchMargin times their threat.
        /// </summary>
        public static void BeforeSightCheck(Character c)
        {
            if (!Enabled || c == null || !c.alive) return;
            var list = c.charactersInViewRange;
            // A friendly NPC is each player's own: it doesn't see the joiners' stand-ins. One the host runs as an
            // enemy (Combat.HostileNpcs) sees everyone.
            if (c.npc != null && !NetEnemies.Eligible(c))
            {
                // A follower a joiner recruited follows them, not the host's player (World.NpcFollowers).
                if (World.NpcFollowers.BeforeSightCheck(c)) return;
                list.RemoveAll(col => col != null && col.GetComponent<NetPlayerProxy>() != null);
                return;
            }
            if (list.Count < 2) return;
            // Leave it be while it's after something other than a player: a door, a lure, another faction.
            if (c.target != null && !Proxies.IsPlayer(c.target.GetComponent<CharBase>())) return;

            var table = Of(c);
            table.Decay();
            float sight = Mathf.Max(1f, c.farViewDistance * c.aniSightRangeModifier);
            var players = new List<(Collider Col, CharBase Who, float Score)>();
            var others = new List<Collider>();
            foreach (var col in list)
            {
                var cb = col != null ? col.GetComponent<CharBase>() : null;
                if (Proxies.IsPlayer(cb) && cb.alive) players.Add((col, cb, Score(table, c, cb, sight)));
                else others.Add(col);
            }
            if (players.Count < 2) return;

            players.Sort((a, b) => a.Score.CompareTo(b.Score));
            // Hysteresis: keep the current target on top unless the best beats it by the margin.
            int cur = players.FindIndex(p => c.target == p.Who.transform);
            var top = players[players.Count - 1];
            if (cur >= 0 && cur != players.Count - 1 && top.Score < players[cur].Score * SwitchMargin)
            {
                var keep = players[cur];
                players.RemoveAt(cur);
                players.Add(keep);
            }
            var chosen = players[players.Count - 1];
            if (Debug && (c.target == null || c.target != chosen.Who.transform))
                Plugin.Log.LogInfo($"[threat] {c.name} prefers {Name(chosen.Who)} ({chosen.Score:0.0}) over " +
                                   string.Join(", ", players.GetRange(0, players.Count - 1).ConvertAll(p => $"{Name(p.Who)} ({p.Score:0.0})")));

            list.Clear();
            list.AddRange(others);
            foreach (var p in players) list.Add(p.Col);
        }

        private static float Score(Table table, Character c, CharBase who, float sight)
        {
            float dist = Vector3.Distance(who.transform.position, c.transform.position);
            return table.Get(who) + CloseWeight * Mathf.Clamp01(1f - dist / sight);
        }

        /// <summary>After the game's sight check: every player it saw gains a little threat for being seen.</summary>
        public static void AfterSightCheck(Character c)
        {
            if (!Enabled || c == null || !c.alive) return;
            var table = Of(c);
            float dt = Mathf.Min(Time.time - table.LastSeenTick, 2f);
            table.LastSeenTick = Time.time;
            foreach (var seen in c.charactersInSight)
                if (Proxies.IsPlayer(seen) && seen.alive && !seen.invisible) table.Add(seen, SeenPerSecond * dt);
        }

        /// <summary>
        /// The player an enemy goes for when the game sends it after "the player" (Character.attackPlayer, enemies
        /// that pursue the player constantly): the one it has the most threat for, closeness counting, seen or not.
        /// </summary>
        public static CharBase BestPlayerFor(Character c)
        {
            var host = Player.Instance;
            // A friendly NPC is each player's own; one the host runs as an enemy goes by threat, as enemies do.
            if (!Enabled || c == null || !NetEnemies.Eligible(c)) return host;
            var table = Of(c);
            table.Decay();
            // Closeness counts within three times its sight, and at least MinCloseRange: an enemy that sees little or
            // nothing (a small farViewDistance, or none while asleep) still goes for the player beside it, not
            // whoever is listed first.
            float range = Mathf.Max(MinCloseRange, c.farViewDistance * c.aniSightRangeModifier * 3f);
            CharBase best = null; float bestScore = float.MinValue, bestDist = float.MaxValue;
            var candidates = new List<CharBase>();
            if (host != null && host.alive && !host.dying) candidates.Add(host);
            foreach (var p in NetPlayerProxy.All.Values) if (p != null && p.alive && !p.invisible) candidates.Add(p); // invisible: as attackCharacter, nobody goes for them
            foreach (var p in candidates)
            {
                float dist = Vector3.Distance(p.transform.position, c.transform.position);
                float score = table.Get(p) + CloseWeight * Mathf.Clamp01(1f - dist / range);
                if (score > bestScore || (score == bestScore && dist < bestDist)) { bestScore = score; bestDist = dist; best = p; } // a tie: the nearer
            }
            return best ?? host;
        }

        /// <summary>For the game's code that aims an enemy at Player.Instance.transform (see ThreatPursuitTranspiler).</summary>
        public static Transform PursuitTarget(Character c)
        {
            var best = BestPlayerFor(c);
            return best != null ? best.transform : Player.Instance.transform;
        }

        /// <summary>
        /// Where "the player" is for the game's distance checks on an enemy (despawning one that is far from the
        /// player, see PlayerPositionTranspiler): the nearest player, the host's own or a living remote one. The
        /// host's own player always counts, alive or not, as in the game.
        /// </summary>
        public static Vector3 NearestPlayerPos(Character c)
        {
            var host = Player.Instance;
            if (host == null) return c != null ? c.transform.position : Vector3.zero;
            Vector3 best = host.transform.position;
            if (!Enabled || c == null) return best;
            Vector3 me = c.transform.position;
            float bestDist = Core.trueDistance(best, me);
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null || !p.alive) continue;
                float d = Core.trueDistance(p.transform.position, me);
                if (d < bestDist) { bestDist = d; best = p.transform.position; }
            }
            return best;
        }

        /// <summary>
        /// The position an enemy runs away from when the game says "from the player": the player it is after, if its
        /// target is a player, or else the nearest player.
        /// </summary>
        public static Vector3 FleeFrom(Character c)
        {
            if (Enabled && c != null && c.target != null && Proxies.IsPlayer(c.target.GetComponent<CharBase>()))
                return c.target.position;
            return NearestPlayerPos(c);
        }

        private static string Name(CharBase c) => c is NetPlayerProxy p ? p.OwnerName : c is Player ? "host player" : c.name;

    }
}
