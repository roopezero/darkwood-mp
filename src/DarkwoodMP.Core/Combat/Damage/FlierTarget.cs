namespace DarkwoodMP.Combat.Damage
{
    /// Whom a diving flier hits (AreaHitRules.Dive).
    public enum FlierTarget : byte
    {
        /// No stand-in and no player of ours: nobody.
        Nobody = 0,

        /// Its target is a stand-in: PlayerDamage to that player.
        StandIn,

        /// Player.Instance.getHit, as the game does.
        OurPlayer,
    }
}
