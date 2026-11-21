using System.Collections.Generic;

namespace DarkwoodMP.Combat.Nights
{
    /// Night attackers and night worms, for every player (the shell's
    /// NightSpawns). The game spawns both around Player.Instance only; the
    /// host does the same around each remote player's stand-in, and
    /// joiners spawn neither. Every chance is rolled by the shell, on the
    /// host, and comes in as a fact.
    public static class NightRules
    {
        /// How far from its target an attacker is spawned
        /// (spawnCharacterAround's radius).
        public const float AttackerAround = 1500f;

        /// The nearest a night worm is spawned to its target.
        public const float WormNear = 1500f;

        /// The furthest a night worm is spawned from its target.
        public const float WormFar = 2000f;

        /// How often the worm loop runs, in scaled seconds.
        public const float WormEvery = 5f;

        /// How far from a position WhereAmI looks for a location's
        /// collider (NightSpawns.BigLocationAt).
        public const float PlaceRadius = 300f;

        /// The layers WhereAmI looks for locations on.
        public const int PlaceLayers = 73402368;

        /// A roll over this (Random.Range(0, 1)) gives a later chapter's
        /// attacker shadow armour.
        public const float ArmorRoll = 0.5f;

        /// The last chapter whose attackers never get shadow armour.
        public const int FirstChapter = 1;

        /// Who sends the attackers when the game's spawnNightChar runs.
        public static NightWay WhoSpawns(bool clientMode, bool anyProxies)
        {
            if (clientMode)
            {
                return NightWay.None;
            }

            if (!anyProxies)
            {
                return NightWay.Game;
            }

            return NightWay.Ours;
        }

        /// Host: the occupied hideouts, each with whom its attacker is
        /// aimed at. The host's player's hideout comes first (none while
        /// in an outside location, as the game checks it); then each
        /// living stand-in, not in a separate location, at a hideout not
        /// listed yet. Nothing with no spawner, nocturnal spawns off or the
        /// host dreaming.
        public static List<NightTarget> Hideouts(
            in NightFacts night,
            IReadOnlyList<NightPlayer> proxies)
        {
            var hideouts = new List<NightTarget>();
            if (!night.HasSpawner || !night.SpawnsNocturnal)
            {
                return hideouts;
            }

            if (night.Dreaming)
            {
                return hideouts;
            }

            bool hostIn = night.HostAtPlace && night.HostBase;
            if (!night.HostOutside && hostIn)
            {
                hideouts.Add(new NightTarget
                {
                    Place = night.HostPlace,
                    Host = true,
                });
            }

            for (int i = 0; i < proxies.Count; i++)
            {
                var p = proxies[i];
                if (!p.Alive || p.Separate || !p.AtPlace || !p.Base)
                {
                    continue;
                }

                if (Listed(hideouts, p.Place))
                {
                    continue;
                }

                hideouts.Add(new NightTarget
                {
                    Place = p.Place,
                    Proxy = i,
                });
            }

            return hideouts;
        }

        /// Which of the scenario's attackers goes next at a hideout: the
        /// first with a name, an amount over 0 and fewer than that out
        /// there. -1 when none. If its spawn fails, nobody goes.
        public static int PickAttacker(IReadOnlyList<NightEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrEmpty(e.Name) || e.Amount <= 0)
                {
                    continue;
                }

                if (e.Out >= e.Amount)
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        /// Whether the world is past the first chapter: the generator's
        /// chapter in a random world, else the scene's location's.
        public static bool LaterChapter(in ChapterFacts world)
        {
            if (world.RandomGeneration)
            {
                return world.GeneratedChapter > FirstChapter;
            }

            return world.HasScene && world.SceneChapter > FirstChapter;
        }

        /// Whether a later chapter's attacker gets shadow armour, by the
        /// host's roll. The shell rolls only in a later chapter, as the
        /// game's code does.
        public static bool GetsShadowArmor(float roll) => roll > ArmorRoll;

        /// Whether an attacker gone is one fewer out at its hideout. Only
        /// at night: one removed by day isn't counted.
        public static bool CountsGone(bool day) => !day;

        /// Who gets a worm at a tick. Nobody on a client, with no spawner,
        /// no host player, on a night that isn't hard, by day, or while
        /// the host's player dreams or ignores night sickness: then nobody
        /// gets one, the joiners neither.
        public static WormDue WormsFor(in WormFacts tick)
        {
            if (tick.ClientMode || !tick.HasSpawner || !tick.HasHost)
            {
                return WormDue.None;
            }

            if (!tick.HardNight || tick.Day)
            {
                return WormDue.None;
            }

            if (tick.Dreaming || tick.IgnoresSickness)
            {
                return WormDue.None;
            }

            if (tick.HostWard)
            {
                return WormDue.Proxies;
            }

            return WormDue.HostAndProxies;
        }

        /// The worm loop sends a worm at this stand-in: alive, in the
        /// world (not in a separate location the host runs for them) and
        /// without a shadow ward.
        public static bool WormWanted(bool alive, bool separate, bool ward)
        {
            return alive && !separate && !ward;
        }

        /// A worm sent at a stand-in attacks on sight, unless it is
        /// defensive.
        public static bool WormOnSight(bool defensive) => !defensive;

        /// Which location collider is the one at a position: the nearest
        /// by Core.trueDistance, a tie to the first. -1 when none.
        public static int NearestPlace(IReadOnlyList<float> distances)
        {
            int closest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < distances.Count; i++)
            {
                if (distances[i] < best)
                {
                    best = distances[i];
                    closest = i;
                }
            }

            return closest;
        }

        static bool Listed(List<NightTarget> hideouts, int place)
        {
            for (int i = 0; i < hideouts.Count; i++)
            {
                if (hideouts[i].Place == place)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
