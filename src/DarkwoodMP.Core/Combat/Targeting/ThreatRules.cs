using System;
using System.Collections.Generic;

namespace DarkwoodMP.Combat.Targeting
{
    /// Threat-based targeting on the host (the plugin's Combat.Threat).
    /// Each enemy keeps a threat value per player (ThreatTable): damage
    /// they dealt it, noise they made that it heard, and time it saw them.
    /// Closeness counts only at choosing time, up to CloseWeight, so an
    /// enemy goes by threat, never simply for the closest player.
    public static class ThreatRules
    {
        /// Threat per unit of a heard noise's volume.
        public const float NoiseWeight = 6f;

        /// The least volume a heard noise counts as.
        public const float MinVolume = 0.1f;

        /// Threat per second an enemy sees a player.
        public const float SeenPerSecond = 2f;

        /// The longest span one seen gain covers.
        public const float MaxSeenSpan = 2f;

        /// Threat for a player right next to the enemy, falling to nothing
        /// at the edge of the closeness range. Not stored.
        public const float CloseWeight = 25f;

        /// The least closeness range in the sight check.
        public const float MinSightRange = 1f;

        /// The least closeness range when choosing "the player".
        public const float MinCloseRange = 1500f;

        /// How many times its sight the closeness range is when choosing
        /// "the player".
        public const float CloseRangeSights = 3f;

        /// Seconds for stored threat to fall to half.
        public const float HalfLife = 15f;

        /// Stored threat that falls below this at a decay is forgotten.
        public const float MinStored = 0.05f;

        /// How many times the current target's score another player needs
        /// for the enemy to turn to them in the sight check.
        public const float SwitchMargin = 1.3f;

        /// The view-range entries the sight check needs to order anything.
        public const int MinSeen = 2;

        static readonly Comparison<SightCandidate> ByScore = CompareScores;

        static readonly Predicate<SightCandidate> Current = IsCurrent;

        /// Threat is on only while the host has a stand-in (a joiner, or
        /// the test dummy); alone, the game's AI is left as it is.
        public static bool IsOn(int standIns) => standIns > 0;

        /// Whether a hit gives its attacker threat (Threat.OnDamaged): when
        /// threat is on and the attacker is a player. The threat is the
        /// damage dealt, times 1; ThreatTable.Add ignores 0 and less.
        public static bool CountsHit(bool on, bool byPlayer) =>
            on && byPlayer;

        /// What a heard noise gives its maker (Threat.OnHeard): the volume,
        /// at least MinVolume, times NoiseWeight, however far away it was.
        /// The caller checks that threat is on and a player made it.
        public static float NoiseGain(float volume) =>
            Max(MinVolume, volume) * NoiseWeight;

        /// The span since the last seen gain, at most MaxSeenSpan.
        public static float SeenSpan(Seconds now, Seconds last) =>
            Min(now.Value - last.Value, MaxSeenSpan);

        /// What being seen for a span gives a player.
        public static float SeenGain(float span) => SeenPerSecond * span;

        /// After a sight check (Threat.AfterSightCheck): whether the seen
        /// gains run for this enemy. Friendly NPCs count too.
        public static bool CountsSeen(in EnemyFacts enemy) =>
            enemy.On && enemy.Exists && enemy.Alive;

        /// Whether one character the enemy saw gains seen threat: a
        /// player, alive and not invisible.
        public static bool GainsSeen(
            bool player,
            bool alive,
            bool invisible) =>
            player && alive && !invisible;

        /// Before the game's sight check (Threat.BeforeSightCheck): what
        /// to do with the enemy's view-range list. A friendly NPC is
        /// handled before the count check, so a lone stand-in is taken out
        /// too.
        public static SightStep BeforeSight(in EnemyFacts enemy)
        {
            if (!enemy.On || !enemy.Exists || !enemy.Alive)
            {
                return SightStep.Leave;
            }

            if (enemy.Npc && !enemy.Eligible)
            {
                return SightStep.FriendlyNpc;
            }

            if (enemy.Seen < MinSeen)
            {
                return SightStep.Leave;
            }

            if (enemy.HasTarget && !enemy.TargetIsPlayer)
            {
                return SightStep.Leave;
            }

            return SightStep.Reorder;
        }

        /// Whether an entry of the view-range list is ordered by threat: a
        /// living player. Invisible ones count here; the dead and everyone
        /// else keep their places in front.
        public static bool InSightOrder(bool player, bool alive) =>
            player && alive;

        /// The closeness range in the sight check: the enemy's far sight
        /// (farViewDistance times aniSightRangeModifier), at least
        /// MinSightRange.
        public static float SightRange(float far, float modifier) =>
            Max(MinSightRange, far * modifier);

        /// The closeness range when choosing "the player": three times the
        /// far sight, at least MinCloseRange.
        public static float CloseRange(float far, float modifier) =>
            Max(MinCloseRange, far * modifier * CloseRangeSights);

        /// Stored threat plus closeness: CloseWeight times how far inside
        /// the range the player is, from 1 beside the enemy to 0 at the
        /// range and beyond.
        public static float Score(float stored, float distance, float range) =>
            stored + CloseWeight * Clamp01(1f - distance / range);

        /// Orders the living players of a view-range list, least first, so
        /// the game's sight check (which takes the last visible one) picks
        /// the one with the most threat it can see. The current target
        /// stays last unless the best scores at least SwitchMargin times
        /// as much. Sets each Score. Returns false, changing nothing, with
        /// fewer than two. The sort isn't stable: equal scores come out in
        /// no set order.
        public static bool Order(List<SightCandidate> players, float range)
        {
            if (players.Count < MinSeen)
            {
                return false;
            }

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                players[i] = p with
                {
                    Score = Score(p.Stored, p.Distance, range),
                };
            }

            players.Sort(ByScore);
            int last = players.Count - 1;
            int cur = players.FindIndex(Current);
            if (cur >= 0
                && cur != last
                && players[last].Score < players[cur].Score * SwitchMargin)
            {
                var keep = players[cur];
                players.RemoveAt(cur);
                players.Add(keep);
            }

            return true;
        }

        /// Whether the enemy picks "the player" by threat
        /// (Threat.BestPlayerFor); if not, it's the host's player.
        public static bool ChoosesByThreat(in EnemyFacts enemy) =>
            enemy.On && enemy.Exists && enemy.Eligible;

        /// "The player" for an enemy: the highest score among the
        /// candidates that count, the nearer on an equal score, the
        /// earlier on both. No margin. The host counts unless dead or
        /// dying (invisible or not); a stand-in unless dead or invisible.
        /// Returns the index in the list, or -1 for the host's player when
        /// none counts.
        public static int Best(List<PlayerCandidate> players, float range)
        {
            int best = -1;
            float bestScore = float.MinValue;
            float bestDist = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!Counts(p))
                {
                    continue;
                }

                float score = Score(p.Stored, p.Distance, range);
                if (score > bestScore
                    || (score == bestScore && p.Distance < bestDist))
                {
                    bestScore = score;
                    bestDist = p.Distance;
                    best = i;
                }
            }

            return best;
        }

        /// Where "the player" is for the game's distance checks on an
        /// enemy (Threat.NearestPlayerPos): the nearest of the host's
        /// player, alive or not, and the living stand-ins; the host on a
        /// tie. With no host player, the enemy's own position (the origin
        /// with no enemy).
        public static Vec3 Nearest(
            in NearestFacts near,
            List<StandInSpot> standIns)
        {
            if (!near.HasHost)
            {
                if (near.Exists)
                {
                    return near.EnemyAt;
                }

                return new Vec3(0f, 0f, 0f);
            }

            var best = near.HostAt;
            if (!near.On || !near.Exists)
            {
                return best;
            }

            float bestDist = near.HostDistance;
            for (int i = 0; i < standIns.Count; i++)
            {
                var p = standIns[i];
                if (!p.Alive)
                {
                    continue;
                }

                if (p.Distance < bestDist)
                {
                    bestDist = p.Distance;
                    best = p.At;
                }
            }

            return best;
        }

        /// Whether an enemy told to flee "from the player" flees from its
        /// target (Threat.FleeFrom); if not, from the nearest player.
        public static bool FleesFromTarget(in EnemyFacts enemy) =>
            enemy.On && enemy.Exists && enemy.HasTarget
            && enemy.TargetIsPlayer;

        static bool Counts(PlayerCandidate p)
        {
            if (p.Host)
            {
                return p.Alive && !p.Dying;
            }

            return p.Alive && !p.Invisible;
        }

        static bool IsCurrent(SightCandidate p) => p.Current;

        static int CompareScores(SightCandidate a, SightCandidate b) =>
            a.Score.CompareTo(b.Score);

        /// As Mathf.Clamp01.
        static float Clamp01(float v)
        {
            if (v < 0f)
            {
                return 0f;
            }

            if (v > 1f)
            {
                return 1f;
            }

            return v;
        }

        /// As Mathf.Max: a unless b is larger.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Min: a unless b is smaller.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }
    }
}
