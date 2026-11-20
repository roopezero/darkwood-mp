using System.Collections.Generic;

namespace DarkwoodMP.World
{
    /// How every game names a world object the same way, and finds it
    /// again from that name (the plugin's ObjRef.Of and Resolve).
    public static class ObjRefRules
    {
        /// How far from its named spot a Placed object is still found.
        public const float PlacedSlack = 50f;

        /// The least time between two scans of the scene for one type.
        public const float PlacedRescan = 0.25f;

        /// When a scan table that was never filled was made: long ago.
        public const float NeverScanned = -999f;

        /// The most squared distance from its spot at which a found
        /// object stands on it.
        public const float OnSpot = 1f;

        /// Bits for each of x and z in a Placed key.
        const int Bits = 20;
        const int Half = 1 << (Bits - 1);
        const int Mask = (1 << Bits) - 1;
        const int NameMask = 0xFFFFFF;

        /// The name an object goes by, in this order: none; made while
        /// playing together; one of the host's enemies; a save id below
        /// the send limit; tied to an id the host gave out; a pinned key;
        /// its name and position.
        public static ObjName Of(in NamingFacts obj)
        {
            if (!obj.Exists)
            {
                return default;
            }

            if (obj.Spawned)
            {
                return new ObjName(ObjKind.Spawned, obj.SpawnedId);
            }

            if (obj.Enemy)
            {
                return new ObjName(ObjKind.Enemy, obj.EnemyId);
            }

            if (obj.SaveId > 0 && obj.SaveId < obj.SendLimit)
            {
                return new ObjName(ObjKind.Saved, obj.SaveId);
            }

            if (obj.Shared)
            {
                return new ObjName(ObjKind.Spawned, obj.SharedId);
            }

            if (obj.Pinned)
            {
                return new ObjName(ObjKind.Placed, obj.PinnedKey);
            }

            long key = PlacedKey(obj.NameHash, obj.Pos);
            return new ObjName(ObjKind.Placed, key);
        }

        /// The part of a name's StableHash that a Placed key holds.
        public static int NameKey(int hash)
        {
            return hash & NameMask;
        }

        /// A Placed key: the name's hash, then x and z rounded (halves to
        /// even, as Mathf.RoundToInt) and wrapped into 20 bits each.
        public static long PlacedKey(int hash, Vec2 pos)
        {
            long x = ((int)System.Math.Round(pos.X) + Half) & Mask;
            long z = ((int)System.Math.Round(pos.Z) + Half) & Mask;
            return ((long)NameKey(hash) << (2 * Bits)) | (x << Bits) | z;
        }

        /// The name's hash and the spot a Placed key holds.
        public static PlacedSpot Unpack(long key)
        {
            int name = (int)(key >> (2 * Bits)) & NameMask;
            int x = (int)((key >> Bits) & Mask) - Half;
            int z = (int)(key & Mask) - Half;
            return new PlacedSpot(name, new Vec2(x, z));
        }

        /// The nearest of the live objects of that name to the spot,
        /// within PlacedSlack. On equal distance the later one wins. On
        /// its spot: a squared distance of at most OnSpot.
        public static PlacedMatch Nearest(
            Vec2 spot,
            IReadOnlyList<Vec2> candidates)
        {
            int best = -1;
            float bestD = PlacedSlack * PlacedSlack;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = candidates[i];
                float d = (p.X - spot.X) * (p.X - spot.X)
                    + (p.Z - spot.Z) * (p.Z - spot.Z);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best < 0)
            {
                return default;
            }

            return new PlacedMatch(best, bestD <= OnSpot);
        }

        /// Whether to scan the scene again and search once more: what the
        /// table gave isn't usable, and the table is old enough.
        public static bool ScanAgain(in ScanFacts scan)
        {
            if (scan.Found && scan.OnSpot && scan.HasComponent)
            {
                return false;
            }

            return (scan.Now - scan.ScannedAt).Value > PlacedRescan;
        }

        /// Where Resolve takes the object from, in the code's order.
        public static ObjSource SourceOf(in LookupFacts name)
        {
            switch (name.Kind)
            {
                case ObjKind.Spawned:
                    return SpawnedSource(name);
                case ObjKind.Enemy:
                    return ObjSource.Enemy;
                case ObjKind.Saved:
                    if (name.Id >= name.ResolveLimit)
                    {
                        return ObjSource.Unshared;
                    }

                    return ObjSource.SaveId;
                case ObjKind.Placed:
                    if (name.PinnedAlive)
                    {
                        return ObjSource.Pinned;
                    }

                    return ObjSource.Nearest;
                default:
                    return ObjSource.Nothing;
            }
        }

        /// The enemy net id an Enemy name holds: its low 32 bits.
        public static uint EnemyIdOf(long id)
        {
            return (uint)id;
        }

        /// The save id a Saved name holds: its low 32 bits.
        public static int SaveIdOf(long id)
        {
            return (int)id;
        }

        /// Whether an object may be tied to an id the host gave out
        /// (BindShared): not when it is gone, when the id is tied to
        /// another live object, or when it is tied to another id.
        public static bool MayTie(in TieFacts tie)
        {
            if (!tie.Exists)
            {
                return false;
            }

            if (tie.IdTied && !tie.IdTiedHere)
            {
                return false;
            }

            if (tie.ObjectTied && tie.ObjectId != tie.Id)
            {
                return false;
            }

            return true;
        }

        /// Client: whether what was found by the name and position the
        /// host announced is tied to the announced id. Only one not tied
        /// to any id; one tied to another is still returned, untied.
        public static bool TiesAnnounced(in TieFacts tie)
        {
            return tie.Exists && !tie.ObjectTied && MayTie(tie);
        }

        static ObjSource SpawnedSource(in LookupFacts name)
        {
            if (name.SpawnedAlive)
            {
                return ObjSource.Spawned;
            }

            if (name.SharedAlive)
            {
                return ObjSource.Shared;
            }

            if (name.Announced)
            {
                return ObjSource.Announced;
            }

            return ObjSource.Nothing;
        }
    }
}
