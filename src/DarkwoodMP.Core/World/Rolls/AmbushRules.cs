namespace DarkwoodMP.World.Rolls
{
    /// Host: the day's redneck ambush (RandomEvent spawnRedneck) can be any
    /// player's. The game sends one redneck at the host's player when it's
    /// outdoors and away from any location, from day 2; with others
    /// playing, the host picks at random among every player that holds for.
    public static class AmbushRules
    {
        /// The first day of an ambush (Controller.day).
        public const int FirstDay = 2;

        /// How far around a joiner's stand-in the redneck is spawned.
        public const float Radius = 1500f;

        /// The redneck's stamina (Character.maxStamina).
        public const float Stamina = 1000f;

        /// The redneck spawned for a joiner.
        public const string Prefab = "Redneck02";

        /// Whether the mod handles this RandomEvent.fire: on the host (not
        /// a client), with the threat system on, for a redneck ambush.
        /// Otherwise it's the game's.
        public static bool Ours(bool client, bool threat, bool redneck)
        {
            return !client && threat && redneck;
        }

        /// Whether the game's gate calls requirementsMet(): only when the
        /// event isn't disabled (or is forced) and the call checks them.
        public static bool AsksRequirements(in AmbushGateFacts gate)
        {
            return (!gate.Disabled || gate.Force) && gate.CheckRequirements;
        }

        /// The game's own gate: not disabled (or forced), and the
        /// requirements met (or not checked) and not started today (or
        /// many times a day), or forced. When shut, the game's own fire
        /// returns at once.
        public static bool GameWants(in AmbushGateFacts gate)
        {
            if (gate.Disabled && !gate.Force)
            {
                return false;
            }

            if (gate.Force)
            {
                return true;
            }

            bool met = !gate.CheckRequirements || gate.RequirementsMet;
            return met && (!gate.StartedToday || gate.MultipleTimesPerDay);
        }

        /// Whether it's a day for an ambush: there is a Controller and its
        /// day is FirstDay or later. Otherwise it's the game's.
        public static bool DayAllows(bool controller, int day)
        {
            return controller && day >= FirstDay;
        }

        /// Whether the host's player can be ambushed: alive, outdoors and
        /// away from any location.
        public static bool HostCan(in AmbushHostFacts host)
        {
            if (!host.Exists || !host.Alive || host.Inside)
            {
                return false;
            }

            return host.Placed && !host.AtLocation;
        }

        /// Whether a joiner can be ambushed: its stand-in exists and is
        /// alive (alive), isn't inside a building (JoinerAi.InsideAt) and
        /// isn't at a big location (NightSpawns.BigLocationAt).
        public static bool JoinerCan(bool alive, bool inside, bool atLocation)
        {
            return alive && !inside && !atLocation;
        }

        /// How many players the host rolls among
        /// (UnityEngine.Random.Range(0, this)): every joiner that can be
        /// ambushed and the host's player if it can. 0 when no joiner can,
        /// and then nothing is rolled: the ambush is the game's own.
        public static int Choices(int joiners, bool hostCan)
        {
            if (joiners == 0)
            {
                return 0;
            }

            if (hostCan)
            {
                return joiners + 1;
            }

            return joiners;
        }

        /// The joiner the roll picked, by index, or -1 when it picked the
        /// host's player: then the ambush is the game's own.
        public static int Target(int joiners, int roll)
        {
            if (roll >= joiners)
            {
                return -1;
            }

            return roll;
        }
    }
}
