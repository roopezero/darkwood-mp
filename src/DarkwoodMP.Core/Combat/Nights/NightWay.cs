namespace DarkwoodMP.Combat.Nights
{
    /// Who sends the night's attackers when CharacterSpawner.spawnNightChar
    /// runs (NightCharsPatch).
    public enum NightWay
    {
        /// Nobody: a joiner sends none (the host does it for every player).
        None,

        /// The game's own spawnNightChar: playing alone.
        Game,

        /// The mod's: one attacker for each occupied hideout.
        Ours,
    }
}
