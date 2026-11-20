namespace DarkwoodMP.Combat.Damage
{
    /// A client's hits on the characters the host runs: which go to the
    /// host, and what of them runs here (ClientForwardHitPatch,
    /// ClientHitEffectsPatch and ClientNoPuppetEffectsPatch). The client
    /// predicts: the bar and the blood show at once (NetEnemies.ClientHit).
    public static class ClientHitRules
    {
        /// The order of the checks decides which reason wins.
        public static ClientHit Forward(in ClientHitFacts hit)
        {
            if (!hit.Handled)
            {
                return ClientHit.Game;
            }

            if (!hit.Puppet)
            {
                if (hit.Frozen)
                {
                    return ClientHit.FrozenCopy;
                }

                return ClientHit.AskNpcs;
            }

            if (hit.Damage <= 0f && hit.Effects == 0)
            {
                return ClientHit.NoDamage;
            }

            if (hit.Thrown)
            {
                return ClientHit.Thrown;
            }

            if (hit.Flame && !hit.OurFlame)
            {
                return ClientHit.OthersFlame;
            }

            if (!FromPlayer(hit))
            {
                return ClientHit.NotOurs;
            }

            return ClientHit.ToHost;
        }

        /// By our player: getHit's byPlayer, or an attacker with a Player.
        /// Sent as the hit's fromPlayer, so always true when sent.
        public static bool FromPlayer(in ClientHitFacts hit) =>
            hit.ByPlayer || hit.PlayerAttacker;

        /// Client: whether our player's melee hit notes its sensor's
        /// effects (NetEnemies.HitEffects) to send with it: ClientMode and
        /// a sensor of type player.
        public static bool NotesEffects(bool client, bool playerSensor) =>
            client && playerSensor;

        /// Client: whether the game's CharacterEffects.activate runs. Not
        /// on the host's enemies, frozen copies, or a friendly NPC whose
        /// hit went to the host.
        public static bool RunsEffects(in HostEffectsFacts c)
        {
            if (!c.Handled)
            {
                return true;
            }

            return !c.Character
                || (!c.Puppet && !c.Frozen && !c.Forwarded);
        }
    }
}
