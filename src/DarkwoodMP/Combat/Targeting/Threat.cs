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
    /// ModHost.Threats. These static methods only pass the game's calls on to
    /// it, so the patches that call them didn't change.
    /// </summary>
    public static class Threat
    {
        /// <summary>
        /// Who's making the noise being broadcast right now (set around
        /// Character.alertInArea), or null.
        /// </summary>
        public static CharBase NoiseSource
        {
            get => Current.NoiseSource;
            set => Current.NoiseSource = value;
        }

        /// <summary>
        /// Only while the host has other players; alone, the game's own AI is
        /// left exactly as it was.
        /// </summary>
        public static bool Enabled => Current.Enabled;

        /// <summary>
        /// Log every target change with its scores ("--dwmp-debug-threat").
        /// </summary>
        public static bool Debug
        {
            get => Current.Debug;
            set => Current.Debug = value;
        }

        private static Threats Current => Plugin.Instance.Host.Threats;

        public static ThreatTable<CharBase> Of(Character c) =>
            Current.Of(c);

        public static void OnDamaged(
            Character victim, CharBase attacker, float damage)
        {
            Current.OnDamaged(victim, attacker, damage);
        }

        public static void OnHeard(
            Character listener, Vector3 position, float volume)
        {
            Current.OnHeard(listener, position, volume);
        }

        /// <summary>
        /// Before the game's sight check: the players in its view-range list
        /// put at its end by threat (Threats.BeforeSightCheck).
        /// </summary>
        public static void BeforeSightCheck(Character c)
        {
            Current.BeforeSightCheck(c);
        }

        /// <summary>
        /// After the game's sight check: every player it saw gains a little
        /// threat for being seen.
        /// </summary>
        public static void AfterSightCheck(Character c)
        {
            Current.AfterSightCheck(c);
        }

        /// <summary>
        /// The player an enemy goes for when the game sends it after
        /// "the player": the one it has the most threat for, closeness
        /// counting, seen or not.
        /// </summary>
        public static CharBase BestPlayerFor(Character c) =>
            Current.BestPlayerFor(c);

        /// <summary>
        /// For the game's code that aims an enemy at Player.Instance.transform
        /// (see ThreatPursuitTranspiler).
        /// </summary>
        public static Transform PursuitTarget(Character c)
        {
            var best = BestPlayerFor(c);
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
