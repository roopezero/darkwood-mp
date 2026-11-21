using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// The decisions about things made while playing (the plugin's
    /// Containers): which are shared, when they're sent, which a game takes
    /// from another, and what a client may place.
    public static class SpawnRules
    {
        /// A gasoline trail a player pours. Sent at once, not once at rest:
        /// the pourer's game merges a few into a puddle and removes them
        /// half a second later, so every trail has to reach the others
        /// first.
        public const string GasolineTrail = "Items/GasolineTrail";

        /// What a thrown molotov leaves behind (Explodes.spawnObject). Taken
        /// from the host only: it's also what each game merges gasoline
        /// trails into by itself.
        public const string ThrownGasoline = "Items/Gasoline";

        /// The map marker the game puts for a death bag left in a separate
        /// location (Player.dropBody).
        public const string BagMarker = "Objects/_Unique/deathDrop_marker";

        /// The marker lies flat: made turned this many degrees about x.
        public const float MarkerTilt = 90f;

        /// Seconds before something placed, or a gasoline trail, is sent,
        /// on the scaled clock.
        public const float PlacedDelay = 0.2f;

        /// Seconds before something dropped is sent, once it has come to
        /// rest, on the scaled clock.
        public const float RestDelay = 1.5f;

        /// Seconds an enemy has been dead before the host sends its corpse's
        /// loot, on the scaled clock (NetEnemies.HostDeadSince).
        public const float CorpseDelay = 2f;

        /// What's shared when made (Containers.SharedPrefabs): dropped
        /// items, what a dead player left behind, gasoline trails.
        static readonly HashSet<string> Shared = new HashSet<string>
        {
            "Items/DroppedItem",
            "Items/DroppedItem_water",
            "Objects/_Unique/deathDrop",
            "Objects/_Unique/deathDrop_water",
            GasolineTrail,
        };

        /// Whether things made from this prefab are shared, placed or not
        /// (Containers.SharedPrefabs).
        public static bool IsShared(string prefab)
        {
            return Shared.Contains(prefab);
        }

        /// Whether a client takes this prefab from the host as what a thrown
        /// item left behind (Containers.ThrownSpawns).
        public static bool IsThrownSpawn(string prefab)
        {
            return prefab == ThrownGasoline;
        }

        /// Whether something the game made is shared (Containers.Made): a
        /// shared kind or something our player placed, made by us (not a
        /// replay) while the world is shared, and not in a dream of our own.
        public static bool Shares(in MadeFacts made)
        {
            if (!made.Exists || made.Replaying || !made.Sharing)
            {
                return false;
            }

            if (!IsShared(made.Prefab) && !made.Placing)
            {
                return false;
            }

            return !made.OursAlone;
        }

        /// How long after it was made something is sent (Containers.Made):
        /// soon when placed or a gasoline trail, else once at rest.
        public static Seconds SendDelay(bool placing, string prefab)
        {
            if (placing || prefab == GasolineTrail)
            {
                return new Seconds(PlacedDelay);
            }

            return new Seconds(RestDelay);
        }

        /// Whether a thing waiting to be sent is due
        /// (Containers.SendPendingSpawns).
        public static bool Due(Seconds now, Seconds sendAt)
        {
            return !(now < sendAt);
        }

        /// Whether a thing is sent as a request the host answers (a client's
        /// placed thing, removed when refused) instead of as it is.
        public static bool AsksHost(bool placed, bool isClient)
        {
            return placed && isClient;
        }

        /// What Containers.MadeFrom does with something the game made from
        /// a prefab object: what a thrown item left comes first; then only
        /// what our player places is shared, by its placeable path.
        public static MadeFromStep FromPrefab(in MadeFromFacts made)
        {
            if (made.Thrown)
            {
                return MadeFromStep.Thrown;
            }

            if (!made.Placing || !made.Exists || made.Replaying)
            {
                return MadeFromStep.Ignore;
            }

            if (!made.Sharing)
            {
                return MadeFromStep.Ignore;
            }

            if (!made.Placeable)
            {
                return MadeFromStep.NotPlaceable;
            }

            return MadeFromStep.Share;
        }

        /// Whether what a thrown item left behind is sent: only by the
        /// host, while the world is shared (Containers.ShareThrownSpawn).
        public static bool SharesThrown(bool exists, bool isHost, bool sharing)
        {
            return exists && isHost && sharing;
        }

        /// What to do with something another game made
        /// (Containers.OnSpawn). Only a shared kind, a placed thing, or (on
        /// a client) what a thrown item left, and only once. The host takes
        /// a client's placed thing only in a request, of a placeable prefab,
        /// within reach, and while its character carries an item that
        /// places it (allowed while the host has no character from it).
        /// Then the prefab has to exist here. The checks go in this order.
        public static SpawnAnswer Receive(in SpawnFacts spawn)
        {
            bool thrown = spawn.IsClient && IsThrownSpawn(spawn.Prefab);
            bool kind = IsShared(spawn.Prefab) || spawn.Placed || thrown;
            if (!kind || spawn.Known)
            {
                return new SpawnAnswer { Step = SpawnStep.Ignore };
            }

            bool counts = false;
            if (spawn.IsHost && spawn.Placed)
            {
                if (!spawn.Handling)
                {
                    return new SpawnAnswer { Step = SpawnStep.Unasked };
                }

                if (!spawn.Placeable)
                {
                    return Refuse(Refusal.NotPlaceable, spawn.Prefab, false);
                }

                if (spawn.Reach.Refused)
                {
                    return new SpawnAnswer
                    {
                        Step = SpawnStep.Refuse,
                        Verdict = spawn.Reach,
                    };
                }

                if (spawn.CharacterKnown && spawn.Carried < 1)
                {
                    return Refuse(Refusal.NotCarried, spawn.Prefab, false);
                }

                counts = spawn.CharacterKnown;
            }

            if (!spawn.PrefabExists)
            {
                return Refuse(Refusal.NoSuchPrefab, spawn.Prefab, counts);
            }

            return new SpawnAnswer
            {
                Step = SpawnStep.Make,
                CountsPlaced = counts,
                Thrown = thrown,
            };
        }

        /// Whether a death bag's map marker is made here: the bag is here,
        /// and has none yet (Containers.OnBagMarker).
        public static bool TakesMarker(bool bagHere, bool hasMarker)
        {
            return bagHere && !hasMarker;
        }

        static SpawnAnswer Refuse(Refusal reason, string prefab, bool counts)
        {
            return new SpawnAnswer
            {
                Step = SpawnStep.Refuse,
                Verdict = Verdict.Refuse(reason, prefab),
                CountsPlaced = counts,
            };
        }
    }
}
