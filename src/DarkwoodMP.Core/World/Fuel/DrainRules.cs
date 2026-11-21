namespace DarkwoodMP.World.Fuel
{
    /// How fast a generator burns fuel with several players
    /// (GeneratorDrainSkillPatch): the electrician skill of the nearest
    /// player counts.
    public static class DrainRules
    {
        /// The modifier of a player with the electrician skill.
        public const float Electrician = 0.5f;

        /// The modifier of a player without it.
        public const float NoSkill = 1f;

        /// What Generator.drainFuel does with the modifier. A sharing
        /// client comes first, then the host with players in.
        public static DrainStart Start(in DrainFacts drain)
        {
            if (drain.Session && drain.Client && drain.Player && drain.Sharing)
            {
                return DrainStart.Zero;
            }

            if (!drain.Session || !drain.Host || !drain.Player)
            {
                return DrainStart.Leave;
            }

            if (!drain.Proxies || !drain.Characters)
            {
                return DrainStart.Leave;
            }

            return DrainStart.Nearest;
        }

        /// The index in Players of the player nearest the generator, on the
        /// ground plane, or -1 for the host's own. A stand-in that is gone,
        /// away, in a location or the test dummy doesn't count. Only a
        /// strictly nearer one wins, so the host wins a tie, and so does
        /// the first of two stand-ins.
        public static int Nearest(in NearestFacts drain)
        {
            float best = float.MaxValue;
            if (!drain.HostAway)
            {
                best = Vec2.Distance(drain.Host, drain.Generator);
            }

            int nearest = -1;
            var players = drain.Players;
            int count = 0;
            if (players != null)
            {
                count = players.Count;
            }

            for (int i = 0; i < count; i++)
            {
                var p = players[i];
                if (!p.Exists || p.Away || p.InLocation || p.Dummy)
                {
                    continue;
                }

                float d = Vec2.Distance(p.Pos, drain.Generator);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// The modifier for the nearest player's skill. When the nearest is
        /// the host's own, or its skill isn't known, the host's own
        /// modifier stays.
        public static float Modifier(bool electrician)
        {
            if (electrician)
            {
                return Electrician;
            }

            return NoSkill;
        }
    }
}
