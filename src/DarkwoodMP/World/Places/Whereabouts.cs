using DarkwoodMP.World.Dreaming;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Places
{
    /// <summary>
    /// Where a player is: the shared world, a separate location (by its
    /// grid's name) or a dream. Players are in the same place when both are in
    /// the shared world, or both in the same location (by name). Dreams are
    /// each player's own, except the shared level dream (DreamSync): players
    /// in it see each other there. These read only the game, so they
    /// work with no session too; what was given up on comes from the
    /// session's Locations.
    /// </summary>
    internal static class Whereabouts
    {
        /// <summary>
        /// Host: whether our own player is in this separate location now.
        /// </summary>
        public static bool OursNow(Location l)
        {
            var o = Singleton<OutsideLocations>.Instance;
            bool inside = l != null && o != null && o.playerInOutsideLocation;
            string ours = null;
            string theirs = null;
            if (inside)
            {
                string current = o.currentLocationName;
                if (current == null)
                {
                    current = "";
                }

                ours = Core.getTrueLocationName(current);
                theirs = Core.getTrueLocationName(l.name);
            }

            return PlaceRules.OursNow(inside, ours, theirs);
        }

        /// <summary>
        /// Host: the separate location a joiner who says they're at `place`
        /// (their PlayerState) and stands at pos is in, when we have it at that
        /// spot; the shared level dream when our player dreams it too
        /// (SharedDream); null for the world, any other dream (each player's
        /// own), or a location we haven't got (they loaded it before the host
        /// answered: it stands somewhere else here).
        /// </summary>
        public static Location HostRunsAt(string place, Vector3 pos)
        {
            PlaceKind kind = PlaceRules.Kind(place);
            if (kind == PlaceKind.None)
            {
                return null;
            }

            Location loc = null;
            if (kind == PlaceKind.Dream)
            {
                // The shared level dream, when our player dreams it too: it is
                // ours to run, as a location is.
                loc = SharedDream();
                if (!PlaceRules.RunsDream(loc != null, place, Current()))
                {
                    return null;
                }
            }
            else
            {
                loc = LoadedAt(place);
            }

            if (loc == null)
            {
                return null;
            }

            if (PlaceRules.InReach(VecOf(pos), VecOf(loc.transform.position)))
            {
                return loc;
            }

            return null;
        }

        /// <summary>
        /// Host: the loaded location a joiner's place names, by its key in
        /// spawnedLocations or its true name.
        /// </summary>
        private static Location LoadedAt(string place)
        {
            var o = Singleton<OutsideLocations>.Instance;
            if (o == null)
            {
                return null;
            }

            foreach (var kv in o.spawnedLocations)
            {
                if (kv.Value != null
                    && PlaceRules.Matches(
                        kv.Key, Core.getTrueLocationName(kv.Key), place))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Client: whether the host runs the area our player is in: the world,
        /// or a separate location the host placed (so it has it at the same
        /// spot; one we loaded after the host didn't answer is ours alone), or
        /// the shared level dream (SharedDream; the host dreams it too). Any
        /// other dream is our own.
        /// </summary>
        public static bool HostRunsHere()
        {
            return PlaceRules.HostRunsHere(HereOf());
        }

        private static HereFacts HereOf()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            bool dreaming = d != null && d.dreaming;
            bool shared = false;
            bool dreamGivenUp = false;
            if (dreaming)
            {
                var loc = SharedDream();
                shared = loc != null;
                if (shared)
                {
                    dreamGivenUp = GaveUpOn(loc.name);
                }
            }

            bool inside = o != null && o.playerInOutsideLocation;
            bool givenUp = false;
            if (!dreaming && inside)
            {
                string name = o.currentLocationName;
                if (name == null)
                {
                    name = "";
                }

                givenUp = GaveUpOn(name);
            }

            return new HereFacts
            {
                Dreaming = dreaming,
                SharedDream = shared,
                DreamGivenUp = dreamGivenUp,
                InLocation = inside,
                LocationGivenUp = givenUp,
            };
        }

        /// <summary>
        /// Client: whether we gave up on the location of this name (by it or
        /// its true name); never with no session.
        /// </summary>
        private static bool GaveUpOn(string name)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return false;
            }

            return coop.Locations.GaveUpOn(
                name, Core.getTrueLocationName(name));
        }

        /// <summary>
        /// The dream location our player is in when it is the shared level
        /// dream (DreamSync.Shared), else null. The host dreams it first
        /// and every player's copy stands at the host's spot (OnAsk), so, like
        /// a location the host placed, the host runs its characters and events
        /// and the others get the host's.
        /// </summary>
        public static Location SharedDream()
        {
            var d = Singleton<Dreams>.Instance;
            if (d == null)
            {
                return null;
            }

            string shared = null;
            if (Coop.Current != null)
            {
                shared = Coop.Current.Dreaming.Shared;
            }

            var dream = new DreamFacts
            {
                Dreaming = d.dreaming,
                HasLocation = d.dreamLocation != null,
                Shared = shared,
                HasPreset = d.preset != null,
                Preset = PresetName(d),
            };

            if (PlaceRules.IsSharedDream(dream))
            {
                return d.dreamLocation;
            }

            return null;
        }

        private static string PresetName(Dreams d)
        {
            if (d.preset == null)
            {
                return null;
            }

            return d.preset.name;
        }

        // ---- where a player is ----------------------------------------------

        /// <summary>
        /// Where this game's player is: "" in the shared world, the location's
        /// name (its grid's) in a separate location, "dream:" and the dream's
        /// name in a dream.
        /// </summary>
        public static string Current()
        {
            return PlaceRules.Current(WhereOf());
        }

        private static WhereFacts WhereOf()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            WorldGrid instance = Singleton<WorldGrid>.Instance;
            string g = null;
            if (instance != null)
            {
                WorldGrid.Grid currentGrid = instance.currentGrid;
                if (currentGrid != null)
                {
                    g = currentGrid.name;
                }
            }

            return new WhereFacts
            {
                Dreaming = d != null && d.dreaming,
                InLocation = o != null && o.playerInOutsideLocation,
                Grid = g,
            };
        }

        /// <summary>
        /// Whether a player who is away (or not) at this place is in the same
        /// place as our player.
        /// </summary>
        public static bool SamePlace(bool away, string place)
        {
            string shared = null;
            if (Coop.Current != null)
            {
                shared = Coop.Current.Dreaming.Shared;
            }

            return PlaceRules.SamePlace(new SameFacts
            {
                Away = away,
                Place = place,
                MeAway = Sync.Players.PlayerCapture.LocalAway(),
                DreamShared = shared != null,
                Current = Current(),
            });
        }
    }
}
