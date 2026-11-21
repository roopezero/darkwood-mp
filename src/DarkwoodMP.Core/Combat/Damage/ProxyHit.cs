namespace DarkwoodMP.Combat.Damage
{
    /// What becomes of a hit on a remote player's stand-in (DamageRules.Send).
    public enum ProxyHit : byte
    {
        /// Not sent: we aren't the host.
        NotSent = 0,

        /// A friendly NPC hit it: that player's own copy of the NPC hits
        /// them, so this hit is dropped (logged under Threat.Debug).
        LeftToTheirCopy,

        /// The stand-in is the test dummy: logged and flashed, no session
        /// needed.
        ToDummy,

        /// Sent to the stand-in's player as PlayerDamage.
        Sent,
    }
}
