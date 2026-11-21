using System.Collections.Generic;

namespace DarkwoodMP.Combat.Landings
{
    /// What the host's enemies throw and leave behind, shared (the
    /// shell's EnemyLandings and its patches). A projectile flies and
    /// lands only in the host's game; clients see a harmless copy of its
    /// flight, and the host sends what it left where it landed.
    public static class LandingRules
    {
        /// How often the host checks whether what landed is gone.
        public const float WatchEvery = 0.5f;

        /// A client's copy of a projectile that hit nothing goes after this.
        public const float CopyLife = 4f;

        /// The drag a ranged sensor's copy flies with (as rangedAttack
        /// sets it).
        public const float SensorDrag = 2f;

        /// The spin given to a copy (Rigidbody.AddTorque's y).
        public const float CopySpin = 225000f;

        /// Host: whether to note this throw, ranged attack or landing, to
        /// send what it makes afterwards. The outermost one only, of one of
        /// our tracked enemies, while sharing with peers.
        public static bool Notes(in ThrowWatchFacts start)
        {
            if (start.Busy || !start.HasSource)
            {
                return false;
            }

            if (!start.Host || start.Peers <= 0 || !start.Sharing)
            {
                return false;
            }

            return start.Tracked;
        }

        /// Host: whether a pooled prefab or a prefab made during a noted
        /// throw is one of what it threw: a ThrownItem not listed yet.
        public static bool TakesThrown(
            bool noting,
            bool thrownItem,
            bool listed)
        {
            return noting && thrownItem && !listed;
        }

        /// Host: whether a prefab made during a noted landing is what it
        /// leaves: the landing thing's prefabToSpawnOnLand. Not checked
        /// against the list.
        public static bool TakesLanded(bool noting, bool landPrefab)
        {
            return noting && landPrefab;
        }

        /// Host: whether to start the watch over what landed: something to
        /// watch and no watch running.
        public static bool StartsWatch(int landed, bool watching)
        {
            return landed > 0 && !watching;
        }

        /// Host: whether a landed thing is gone (EnemyLandedGone):
        /// destroyed, or switched off as it ended.
        public static bool Gone(bool exists, bool active)
        {
            return !exists || !active;
        }

        /// Client: whether a character may throw (spawnProjectile). A
        /// frozen character or a copy of the host's enemy throws nothing in
        /// the host's world; the host's throw lands here as EnemyLanded.
        public static bool MayThrow(bool clientMode, bool frozen, bool copy)
        {
            if (!clientMode)
            {
                return true;
            }

            return !frozen && !copy;
        }

        /// Client: whether to handle a message about the host's throws:
        /// in a session, in the host's world, as a client, from the host.
        public static bool FromHost(in HostSendFacts message)
        {
            if (!message.HasSession || !message.InWorld)
            {
                return false;
            }

            return message.IsClient && message.FromHost;
        }

        /// Client: whether a copy made for EnemyProjectile or
        /// EnemySensorThrown is shown: only thrown things (a ThrownItem
        /// with a Rigidbody). Else it is put back or destroyed.
        public static bool ShowsCopy(bool thrownItem, bool body)
        {
            return thrownItem && body;
        }

        /// Client: which of our copy's sensors the host's ranged attack
        /// threw: the first ranged one of that name with a sensor. -1 when
        /// none (a warning).
        public static int SensorIndex(
            IReadOnlyList<SensorChoice> sensors,
            string name)
        {
            for (int i = 0; i < sensors.Count; i++)
            {
                var s = sensors[i];
                if (s.Ranged && s.Name == name && s.HasSensor)
                {
                    return i;
                }
            }

            return -1;
        }

        /// Client: what to do with an EnemyLanded: had is a live thing
        /// with its id here (ObjRef.Spawned), known a prefab of its name.
        public static LandedWay Landed(bool had, bool known)
        {
            if (had)
            {
                return LandedWay.Ignore;
            }

            if (!known)
            {
                return LandedWay.Unknown;
            }

            return LandedWay.Make;
        }
    }
}
