namespace DarkwoodMP.Combat.Noise
{
    /// Whose noise a Character.alertInArea is (NoiseRules.Owner).
    public enum NoiseOwner : byte
    {
        /// Whoever raised it set Threat.NoiseSource already (a remote
        /// player's noise): left as it is.
        Set = 0,

        /// Not our player's: nothing is marked.
        NotOurs,

        /// Our player's: Threat.NoiseSource is set to Player.Instance.
        Ours,

        /// Our player's, and we are a client: PlayerNoise goes to the host
        /// first, then Threat.NoiseSource is set.
        OursToHost,
    }
}
