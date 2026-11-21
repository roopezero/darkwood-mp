namespace DarkwoodMP.Combat.Beams
{
    /// Shadow armour after the game's light check on the host
    /// (ShadowArmor.checkIfInLight, ShadowArmorJoinerLightPatch).
    public readonly struct ArmorFacts
    {
        /// The armour isn't null.
        public bool Exists { get; init; }

        /// ShadowArmor.health.
        public float Health { get; init; }

        /// How many stand-ins the host has (NetPlayerProxy.All.Count).
        public int StandIns { get; init; }

        /// Session.Instance isn't null and is the host.
        public bool Hosting { get; init; }
    }
}
