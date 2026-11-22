namespace DarkwoodMP.World.Deaths
{
    /// Whether a game may reset its world, with others in it (the plugin's
    /// DeathRules). A client never does: the host runs the enemies and the
    /// clock. The host doesn't from inside its own death while others play,
    /// who may still be fighting; alone it does, as in single player.
    public static class DeathRules
    {
        /// Whether the world may be reset. A client is refused for every
        /// caller, not only a death.
        public static bool WorldResetAllowed(in DeathFacts death)
        {
            if (death.ClientMode)
            {
                return false;
            }

            if (death.IsHost && death.Peers > 0 && death.InDeath)
            {
                return false;
            }

            return true;
        }

        /// Whether WorldResetAllowed looks at InDeath, so that the shell
        /// reads the stack only then, as today: a host with peers.
        public static bool NeedsStack(in DeathFacts death)
        {
            return !death.ClientMode && death.IsHost && death.Peers > 0;
        }
    }
}
