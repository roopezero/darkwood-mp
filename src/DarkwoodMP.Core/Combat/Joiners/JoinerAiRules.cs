using System.Collections.Generic;

namespace DarkwoodMP.Combat.Joiners
{
    /// The game's enemy code that reacts only to the host's player, made
    /// to react to a joiner's stand-in too (the plugin's JoinerAi and its
    /// patches, on the host).
    public static class JoinerAiRules
    {
        /// How near a living stand-in counts for inSightOrCloseToPlayer,
        /// with no line of sight (the game uses 1000 for the host's).
        public const float NearRange = 1500f;

        /// The brother's weak attack starts below this much health.
        public const float WeakHealth = 20f;

        /// The health the brother rule assumes with no host player.
        public const float DefaultMaxHealth = 100f;

        /// The brother needs this many attacks for the switch.
        public const int WeakAttacks = 3;

        /// A stand-in bumping into an enemy, in this order: the game's
        /// method alone unless threat is on, this game hosts the enemies,
        /// the enemy is active and the collider is a living stand-in that
        /// isn't away. Then a banshee attacks, a sleeper that wakes by
        /// itself wakes, and any other reacts unless the stand-in is
        /// invisible or the enemy dies on contact.
        public static BumpStep Bump(in BumpFacts bump)
        {
            if (!bump.Collider
                || !bump.On
                || bump.ClientMode
                || !bump.Active)
            {
                return BumpStep.Game;
            }

            if (!bump.StandIn || !bump.StandInAlive || bump.StandInAway)
            {
                return BumpStep.Game;
            }

            if (bump.Banshee)
            {
                return BumpStep.Banshee;
            }

            if (bump.Sleeping && !bump.WakesOnlyByHand)
            {
                return BumpStep.Wake;
            }

            if (!bump.StandInInvisible && !bump.DiesOnContact)
            {
                return BumpStep.React;
            }

            return BumpStep.Touch;
        }

        /// Whether inSightOrCloseToPlayer's postfix looks at the
        /// stand-ins: the game said no and threat is on.
        public static bool LooksForJoiners(bool result, bool on) =>
            !result && on;

        /// Whether a stand-in makes inSightOrCloseToPlayer true: alive,
        /// not away, and nearer than NearRange (Core.trueDistance).
        public static bool IsNear(bool alive, bool away, float distance) =>
            alive && !away && distance < NearRange;

        /// Whether sending a character after a player (JoinerAi.GoFor, and
        /// a joiner's flashlight on shadow armour) makes it attack on
        /// sight: not when it is defensive.
        public static bool AttacksOnSight(bool defensive) => !defensive;

        /// Whether the forest spirit's player is indoors
        /// (JoinerAi.TargetIsInside): a stand-in target's ground, else the
        /// host's player's.
        public static bool TargetInside(
            bool standIn,
            bool standInInside,
            bool hostInside)
        {
            if (standIn)
            {
                return standInInside;
            }

            return hostInside;
        }

        /// Whether a hatching spider joins the host's player's location
        /// (JoinerAi.HatchLocation); if not, the game uses the location
        /// it is in. The host's when threat is off or there is no
        /// character, or when the nearest player's position is the host's
        /// (Unity's approximate Vector3 ==).
        public static bool HatchesAtHost(
            bool on,
            bool exists,
            bool nearestIsHost) =>
            !on || !exists || nearestIsHost;

        /// Whose location a spawned character's activity uses
        /// (JoinerAi.SpawnedLocation): the location it was sent at, else
        /// that of the stand-in it was sent at or of "the player" when a
        /// stand-in, else the host's.
        public static LocationPick SpawnedLocation(in ActivityFacts a)
        {
            if (!a.On || !a.Exists)
            {
                return LocationPick.Host;
            }

            if (a.Sent && a.SentLocation)
            {
                return LocationPick.Sent;
            }

            bool standIn = a.SentStandIn || a.BestStandIn;
            if (!standIn || !a.StandInLocation)
            {
                return LocationPick.Host;
            }

            return LocationPick.StandIn;
        }

        /// Which player an activity aimed at "the player" goes for
        /// (JoinerAi.ActivityPlayer): the living stand-in it was sent at,
        /// else "the player" by threat, else the host's.
        public static PlayerPick ActivityPlayer(in ActivityFacts a)
        {
            if (!a.On || !a.Exists)
            {
                return PlayerPick.Host;
            }

            if (a.Sent && a.SentStandIn && a.SentAlive)
            {
                return PlayerPick.Sent;
            }

            if (a.BestPlayer)
            {
                return PlayerPick.Best;
            }

            return PlayerPick.Host;
        }

        /// Whether checkOutLocation's prefix notes the stand-in it chases:
        /// threat is on, it isn't already checking out, and its
        /// superTarget is a living stand-in on indoor ground.
        public static bool NotesChase(in ChaseFacts chase) =>
            chase.On
            && !chase.CheckingOut
            && chase.StandIn
            && chase.StandInAlive
            && chase.StandInInside;

        /// What gotoNextWaypoint's prefix does: nothing with no noted
        /// chase or a waypoint already given; else it forgets the chase,
        /// and picks a waypoint only when the enemy checks out a location.
        /// It doesn't check that the call is the chasing enemy's.
        public static WaypointStep Waypoint(
            bool chased,
            bool waypoint,
            bool location)
        {
            if (!chased || waypoint)
            {
                return WaypointStep.Leave;
            }

            if (!location)
            {
                return WaypointStep.Forget;
            }

            return WaypointStep.Pick;
        }

        /// The waypoint nearest the stand-in: the first with the least
        /// squared distance, null ones skipped. -1 with none.
        public static int NearestWaypoint(List<WaypointSpot> waypoints)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < waypoints.Count; i++)
            {
                var w = waypoints[i];
                if (!w.Exists)
                {
                    continue;
                }

                if (w.SqrDistance < bestDist)
                {
                    bestDist = w.SqrDistance;
                    best = i;
                }
            }

            return best;
        }

        /// Whether the brother fighting a stand-in switches to its weak
        /// attack (attacks 0 and 1 off, 2 on): the player's health, as a
        /// share of the host's player's maxHealth, is below WeakHealth and
        /// it has WeakAttacks attacks. The game's own check never runs for
        /// a stand-in, so it never switches back.
        public static bool BrotherWeak(in BrotherFacts brother)
        {
            float max = DefaultMaxHealth;
            if (brother.HasHost)
            {
                max = brother.HostMaxHealth;
            }

            return brother.HealthPercent / 100f * max < WeakHealth
                && brother.Attacks >= WeakAttacks;
        }
    }
}
