using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Scripted
{
    /// A world location's enter and exit events with players far apart
    /// (the plugin's FarEvents). On the host, a location entered for a
    /// client runs its world side only, unless the host's player is by it;
    /// a client's events are its own player's.
    public static class FarEventRules
    {
        /// How much nearer the host's player may be to count as in a
        /// location as it is entered, or to still count as in one it was
        /// in (the location's node is up to 300 nearer than it).
        public const float Margin = 600f;

        /// How often the host lists the world locations again (real
        /// time).
        public const float ListEvery = 10f;

        /// Whether a location is a world location: not a sub-location or
        /// an outside one.
        public static bool IsWorldLocation(bool sub, bool outside)
        {
            return !sub && !outside;
        }

        /// Whether the host's player is in a location's cull rect: within
        /// the culling's distances on each axis, plus the margin when it
        /// was in it last time. Never when it has no player or is away.
        public static bool HostNear(in FarFacts f, bool was)
        {
            if (!f.Host.Known || f.Host.Away)
            {
                return false;
            }

            float m = 0f;
            if (was)
            {
                m = Margin;
            }

            return InRect(f.Host.Pos, f, m);
        }

        /// Client: whether another player, not away and not the map test's
        /// dummy, is in the location's cull rect. Their culling isn't
        /// known here, so ours counts.
        public static bool OtherNear(
            IReadOnlyList<RemoteSpot> remotes,
            in FarFacts f)
        {
            for (int i = 0; i < remotes.Count; i++)
            {
                var r = remotes[i];
                if (!r.Shown || r.Away || r.Dummy)
                {
                    continue;
                }

                if (InRect(r.Pos, f, 0f))
                {
                    return true;
                }
            }

            return false;
        }

        /// The part a location's enter events run in now, and the host's
        /// record of whether its player is in it.
        public static FarPart OnEnter(in FarFacts f)
        {
            if (!Shares(f))
            {
                return default;
            }

            if (f.Client)
            {
                return new FarPart
                {
                    Set = true,
                    Part = EventPart.PlayerAndShared,
                };
            }

            bool hostIn = !f.AnyProxies || HostNear(f, true);
            return new FarPart
            {
                Set = !hostIn,
                Part = HostPart(hostIn),
                Record = FarRecord.Store,
                HostIn = hostIn,
            };
        }

        /// The part a location's exit events run in now. On the host, as
        /// the record says (in, when there is none), and the record goes.
        public static FarPart OnExit(in FarFacts f)
        {
            if (!Shares(f))
            {
                return default;
            }

            if (f.Client)
            {
                return new FarPart
                {
                    Set = true,
                    Part = EventPart.PlayerAndShared,
                };
            }

            bool hostIn = true;
            if (f.Recorded)
            {
                hostIn = f.HostIn;
            }

            return new FarPart
            {
                Set = !hostIn,
                Part = HostPart(hostIn),
                Record = FarRecord.Forget,
            };
        }

        /// Whether a location's exit despawns its trader, porter and wolf
        /// here: not on a client while another player is by it.
        public static bool Despawns(
            in FarFacts f,
            IReadOnlyList<RemoteSpot> remotes)
        {
            bool world = IsWorldLocation(f.Sub, f.Outside);
            return !(f.Together && f.Client && world
                && OtherNear(remotes, f));
        }

        /// Host: whether to look at the locations this culling refresh:
        /// in a world that isn't loading, and not dreaming.
        public static bool MayTick(in FarTickFacts f)
        {
            return f.Host && f.InWorld && !f.Loading && !f.Dreaming;
        }

        /// Host: whether to list the world locations again.
        public static bool Relist(in FarTickFacts f)
        {
            return !f.Listed || f.Now.Value - f.ListedAt.Value > ListEvery;
        }

        /// Host: what one location does as its player comes or goes. The
        /// first sight only records; after that the margin holds a player
        /// who was in it.
        public static FarTick Tick(in FarFacts f)
        {
            if (!f.Entered)
            {
                return FarTick.Forget;
            }

            if (!f.Loaded || f.Activating)
            {
                return FarTick.Wait;
            }

            if (!f.Recorded)
            {
                return FarTick.Record;
            }

            bool now = HostNear(f, f.HostIn);
            if (now == f.HostIn)
            {
                return FarTick.Same;
            }

            if (now)
            {
                return FarTick.Came;
            }

            return FarTick.Went;
        }

        /// A location's events are shared when together, for a world
        /// location, with no part being run or replayed.
        static bool Shares(in FarFacts f)
        {
            if (!f.Together || !IsWorldLocation(f.Sub, f.Outside))
            {
                return false;
            }

            return f.Current == EventPart.All && !f.Replaying;
        }

        static EventPart HostPart(bool hostIn)
        {
            if (hostIn)
            {
                return EventPart.All;
            }

            return EventPart.HostAndShared;
        }

        static bool InRect(Vec2 pos, in FarFacts f, float margin)
        {
            return Math.Abs(pos.X - f.At.X) <= f.CullX + margin
                && Math.Abs(pos.Z - f.At.Z) <= f.CullZ + margin;
        }
    }
}
