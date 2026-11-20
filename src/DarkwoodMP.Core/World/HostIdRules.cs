using System.Collections.Generic;
using System.Text;

namespace DarkwoodMP.World
{
    /// Ids the host gives to what the world made by itself after a player
    /// joined, and how a client ties its own copy to one (the plugin's
    /// HostIds). Times are Time.unscaledTime.
    public static class HostIdRules
    {
        /// Seconds: how far apart in time the host's object and a client's
        /// copy may start, and how long an announced id waits.
        public const float Window = 30f;

        /// Seconds: how long after it starts an object is looked at.
        public const float Settle = 1f;

        /// Seconds between two prunings of ties to destroyed objects.
        public const float PruneEvery = 10f;

        /// Seconds: the least age of a client's copy (TryBind), a moment
        /// for its own replays to be named first.
        public const float ReplayNaming = Settle / 2;

        /// Squared distances this close count as the same (Better).
        public const float SameDistance = 1f;

        /// The HostIds flag of ids made before the joiner came: the host
        /// sends every id it gave out to a new joiner with it (SendAllTo).
        public const byte MadeBefore = 1;

        /// An id from 62 random bits: two draws of System.Random.Next.
        public static long NewId(int high, int low)
        {
            return ((long)high << 31) | (uint)low;
        }

        /// Whether an object that started age ago is looked at: if it
        /// NeedsId, given an id (host), or tried against the late ids
        /// (client).
        public static bool Settled(Seconds age)
        {
            return age.Value >= Settle;
        }

        /// Whether a settled object needs an id from the host: it is
        /// alive, and no id names it in every game (ObjRef.Unnamed). The
        /// host gives it one (Update); a client tries it once against
        /// the late ids of its name (TryBindLate).
        public static bool NeedsId(bool alive, bool unnamed)
        {
            return alive && unnamed;
        }

        /// Whether a started object is too old to be anyone's copy.
        public static bool Forgotten(Seconds age)
        {
            return age.Value > Window;
        }

        /// Whether an announced id not tied by until goes to the late ids.
        public static bool Expired(Seconds now, Seconds until)
        {
            return now > until;
        }

        /// Client (OnHostIds): whether the ids of a HostIds message go
        /// straight to the late ids, by name, and are never tried by
        /// time: made before this joiner came, their copies are in the
        /// save it loaded.
        public static bool GoesLate(byte flags)
        {
            return (flags & MadeBefore) != 0;
        }

        /// The squared distance on the ground the candidates compare.
        public static float SquaredDistance(Vec2 a, Vec2 b)
        {
            return (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);
        }

        /// Whether a candidate is within PlacedSlack (squared distances).
        public static bool InSlack(float squared)
        {
            const float Slack = ObjRefRules.PlacedSlack;
            return !(squared > Slack * Slack);
        }

        /// Client (TryBind): whether a started object may be the copy of
        /// an id the host announced, before its distance is looked at.
        public static bool MayBeCopy(in CopyFacts copy)
        {
            if (!copy.Alive
                || copy.Age.Value > Window
                || !copy.SameName
                || copy.Age.Value < ReplayNaming)
            {
                return false;
            }

            return copy.Unnamed || copy.LateTied;
        }

        /// Client (TryBindLate): whether a late id of the object's name
        /// may be its id: not tied to a live object (ObjRef.IsBound), and
        /// within PlacedSlack (squared distances).
        public static bool IsLateCandidate(bool tied, float squared)
        {
            return !tied && InSlack(squared);
        }

        /// Nearer wins; at the same distance (squared distances within
        /// SameDistance), the one in the same place in its hierarchy.
        public static bool Better(in Candidate c, in Candidate best)
        {
            return c.Squared < best.Squared - SameDistance
                || (c.Squared <= best.Squared + SameDistance
                    && c.SamePlace
                    && !best.SamePlace);
        }

        /// The best of the candidates, in their order: the first is taken
        /// before any comparison. -1 when there are none.
        public static int Pick(IReadOnlyList<Candidate> candidates)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (best < 0 || Better(candidates[i], candidates[best]))
                {
                    best = i;
                }
            }

            return best;
        }

        /// The text PlaceOf hashes: from the object up to the root, each
        /// level's name, then ':' and its sibling index unless it is the
        /// root (its place among the scene's roots differs), then '/'.
        public static string PlacePath(IReadOnlyList<PlaceLevel> levels)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < levels.Count; i++)
            {
                sb.Append(levels[i].Name);
                if (i < levels.Count - 1)
                {
                    sb.Append(':').Append(levels[i].Index);
                }

                sb.Append('/');
            }

            return sb.ToString();
        }
    }
}
