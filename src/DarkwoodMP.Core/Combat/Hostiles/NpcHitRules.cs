namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: our player turning a friendly NPC hostile doesn't do it in
    /// our game. The hit goes to the host as NpcHit, a scripted attack as
    /// NpcAttack, and the host's copy turns on our stand-in. When the host
    /// answers NoSuchNpc, the NPC is ours, and the hit or attack happens
    /// here (the shell's HostileNpcs.ClientHit and ClientAttack).
    public static class NpcHitRules
    {
        /// What becomes of our player's hit. The first that applies wins.
        public static NpcHitWay Way(in NpcHitFacts hit)
        {
            if (hit.Ours || !hit.IsNpc)
            {
                return NpcHitWay.Game;
            }

            if (!hit.Alive || hit.NightTrader || hit.LocalOnly)
            {
                return NpcHitWay.Ours;
            }

            if (hit.Thrown)
            {
                return NpcHitWay.Thrown;
            }

            if (hit.Flame && !hit.FlameOurs)
            {
                return NpcHitWay.Game;
            }

            // What the world does to it happens in each game.
            if (!hit.ByPlayer && !hit.AttackerIsPlayer)
            {
                return NpcHitWay.Game;
            }

            // NaN damage isn't at most zero: it is sent.
            if (hit.Damage <= 0f && hit.Effects == 0)
            {
                return NpcHitWay.Game;
            }

            return NpcHitWay.Sent;
        }

        /// What our game does with the host's answer to our NpcHit.
        public static NpcHitAnswer AfterHit(in NpcAnswerFacts answer)
        {
            if (answer.Accepted)
            {
                return NpcHitAnswer.HostApplied;
            }

            if (!NotHere(answer.Reason) || !answer.Alive || !answer.HasPlayer)
            {
                return NpcHitAnswer.Nothing;
            }

            return NpcHitAnswer.OursHere;
        }

        /// What becomes of a scripted attack. The first that applies wins.
        public static NpcAttackWay AttackWay(in NpcAttackFacts attack)
        {
            if (attack.Ours || !attack.IsNpc || attack.Dummy)
            {
                return NpcAttackWay.Game;
            }

            if (!attack.ClientMode || attack.LocalOnly)
            {
                return NpcAttackWay.Game;
            }

            // The host's: it's already hostile there.
            if (attack.Copy || attack.Frozen)
            {
                return NpcAttackWay.Skip;
            }

            // Another player's event, run here too: the host sent its copy
            // after them already.
            if (attack.Replaying || attack.SharedOnly)
            {
                return NpcAttackWay.Skip;
            }

            return NpcAttackWay.Send;
        }

        /// Whether the host's answer to our NpcAttack leaves the attack to
        /// our game: refused as NoSuchNpc, with the NPC there and alive.
        public static bool OursToAttack(
            bool accepted,
            string reason,
            bool alive)
        {
            return !accepted && NotHere(reason) && alive;
        }

        /// The host's words for an NPC it hasn't got. Only the words cross
        /// the wire, so they are compared.
        static bool NotHere(string reason)
        {
            var notHere = Verdict.Refuse(Refusal.NoSuchNpc);
            return reason == Refusals.Text(notHere);
        }
    }
}
