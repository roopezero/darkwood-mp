using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Traders
{
    /// The host's copies of joiners' morning traders (the plugin's
    /// TraderSync.SpawnHostCopy and HostUpdate). A joiner's morning trader
    /// appears in their hideout at dawn; when the host's game has none
    /// there (its player wasn't in that hideout), it puts its own there,
    /// whose stock it rolls, and takes him away when no joiner is in a
    /// morning any more.
    public static class TraderCopyRules
    {
        /// The wolf comes to a hideout in the morning too
        /// (Location.spawnWolf), but he isn't one to trade with: no name
        /// with this in it, in any case.
        public const string Wolf = "wolf";

        /// How far from where the joiner has him a hideout's trader spot
        /// may be, on the ground.
        public const float CopyRange = 300f;

        /// A joiner's PlayerState says they're in a morning a moment after
        /// their trader appears: a copy is kept this long whatever it says.
        public const float CopyGrace = 10f;

        /// How often the host looks at its copies.
        public const float CheckEvery = 1f;

        /// Whether the host may put its own trader of this name in a
        /// joiner's hideout: not the wolf, and not one who is dead in the
        /// host's story (Flags.getNPCState(name).dead).
        public static bool MayCopy(string name, bool dead)
        {
            if (name.IndexOf(Wolf, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return !dead;
        }

        /// The hideout to put him in: the player base whose trader spot is
        /// nearest to where the joiner has him, nearer than CopyRange. -1
        /// for none. Of two at the same distance, the first in the list.
        public static int NearestBase(
            IReadOnlyList<HideoutBase> bases,
            Vec2 at)
        {
            int best = -1;
            float bestSquared = CopyRange * CopyRange;
            if (bases == null)
            {
                return best;
            }

            for (int i = 0; i < bases.Count; i++)
            {
                var hideout = bases[i];
                if (!hideout.PlayerBase || !hideout.HasTraderSpot)
                {
                    continue;
                }

                float d = TraderRules.FlatSquared(hideout.TraderSpot, at);
                if (d < bestSquared)
                {
                    bestSquared = d;
                    best = i;
                }
            }

            return best;
        }

        /// What comes of the hideout's spawnTrader: kept as a copy when
        /// it's the one asked about (spawned is his name, null for none).
        /// Another of the hideout's morning traders is taken away again
        /// unless he was there before (had).
        public static SpawnResult AfterSpawn(
            string asked,
            string spawned,
            bool had)
        {
            if (spawned == null)
            {
                return SpawnResult.NoTrader;
            }

            if (spawned == asked)
            {
                return SpawnResult.Kept;
            }

            if (had)
            {
                return SpawnResult.OtherStays;
            }

            return SpawnResult.OtherGoes;
        }

        /// Whether the host looks at its copies now: it has some, and
        /// the next look is due.
        public static bool Due(int copies, Seconds now, Seconds next)
        {
            if (copies == 0 || now < next)
            {
                return false;
            }

            return true;
        }

        /// When the host looks at its copies again after now.
        public static Seconds Next(Seconds now)
        {
            return new Seconds(now.Value + CheckEvery);
        }

        /// Whether a joiner is in a morning: a remote player, not the
        /// test dummy, whose last state says so (PlayerState.AfterNight).
        public static bool SomeoneInMorning(
            IReadOnlyList<RemoteMorning> remotes)
        {
            if (remotes == null)
            {
                return false;
            }

            for (int i = 0; i < remotes.Count; i++)
            {
                var remote = remotes[i];
                if (!remote.Dummy && remote.HasState && remote.AfterNight)
                {
                    return true;
                }
            }

            return false;
        }

        /// What becomes of one copy at a look. In this order: gone or dead,
        /// forgotten (a body stays, as one does); kept while a joiner is in
        /// a morning, or for CopyGrace after it came; forgotten but left
        /// there when the host's own player is in a morning in that
        /// hideout (ours now); else taken away.
        public static CopyFate Fate(in TraderCopyFacts copy)
        {
            if (copy.Gone || copy.Dead)
            {
                return CopyFate.Forget;
            }

            if (copy.Someone || (copy.Now - copy.Since).Value < CopyGrace)
            {
                return CopyFate.Keep;
            }

            if (copy.OursNow)
            {
                return CopyFate.Forget;
            }

            return CopyFate.TakeAway;
        }
    }
}
