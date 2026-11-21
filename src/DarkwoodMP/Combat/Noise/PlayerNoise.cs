namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Our player's noise (CharacterAlertInArea). It lives as long as the game
    /// (ModHost.Noise): noise is counted as our player's when playing alone
    /// too.
    /// </summary>
    internal sealed class PlayerNoise
    {
        /// <summary>
        /// Above zero inside the game's code that makes noise only for this
        /// game's own player (OwnNoiseMakers): those methods are
        /// patched too, so their stack frames can't be told apart by type.
        /// </summary>
        public int OwnNoise { get; set; }
    }
}
