using DarkwoodMP.Combat.Targeting;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Threat-based targeting, on the host, while there are several players.
    /// Each enemy keeps a threat value for every player it knows of, instead of
    /// going for whoever its sight check happened to list last:
    ///
    ///   damage      every point of damage a player deals to it: x1.0
    ///   noise       sounds a player makes that it hears: their footsteps,
    ///               running and shots (the game's Character.alertInArea,
    ///               raised from Player code; remote players' come from their
    ///               games as PlayerNoise): volume x NoiseWeight
    ///   being seen  every second it can see a player: SeenPerSecond
    ///   closeness   at choosing time, not stored: up to CloseWeight for a
    ///               player right next to it, nothing at the edge of its sight
    ///
    /// Stored threat fades (half-life HalfLife seconds), so an enemy loses
    /// interest in someone who stopped bothering it. It goes for the player
    /// with the most threat among those it can see, and only turns from its
    /// current target to another once that one has SwitchMargin times as much,
    /// so it doesn't flip back and forth.
    ///
    /// The game's own reactions stay: being hit still makes it react to the
    /// attacker at once, and the next sight check decides by threat who it
    /// keeps after. The choice is made by ordering the enemy's view-range list
    /// before its sight check (see BeforeSightCheck), so all of the game's own
    /// targeting follows it.
    ///
    /// The numbers and choices are the core's ThreatRules; the state is
    /// ModHost.Threats, which callers use directly. The game's code that the
    /// transpilers in ThreatPatches and JoinerAiPatches rewrite calls the
    /// static methods left here: a transpiler puts in a static call.
    /// </summary>
    public static class Threat
    {
        private static Threats Current => Plugin.Instance.Host.Threats;

        /// <summary>
        /// For the game's code that aims an enemy at Player.Instance.transform
        /// (see CharacterCanSeeEnemyPursuit).
        /// </summary>
        public static Transform PursuitTarget(Character c)
        {
            var best = Current.BestPlayerFor(c);
            if (best != null)
            {
                return best.transform;
            }

            return Player.Instance.transform;
        }

        /// <summary>
        /// Where "the player" is for the game's distance checks on an enemy
        /// (despawning one that is far from the player, see
        /// PlayerPositionTranspiler): the nearest player, the host's own or a
        /// living remote one. The host's own player always counts, alive or
        /// not, as in the game.
        /// </summary>
        public static Vector3 NearestPlayerPos(Character c) =>
            Current.NearestPlayerPos(c);

        /// <summary>
        /// The position an enemy runs away from when the game says
        /// "from the player": the player it is after, if its target is a
        /// player, or else the nearest player.
        /// </summary>
        public static Vector3 FleeFrom(Character c) => Current.FleeFrom(c);
    }
}
