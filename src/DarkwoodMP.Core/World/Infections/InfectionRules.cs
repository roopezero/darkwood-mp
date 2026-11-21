namespace DarkwoodMP.World.Infections
{
    /// The infection's patches (the plugin's Infections): who sends a
    /// clear, which cleared patches a game takes, and the host's spread.
    public static class InfectionRules
    {
        /// How far from where the sender saw it a patch found without its
        /// ObjRef may be. No name is compared.
        public const float FindRadius = 20f;

        /// How far from a patch a client's player may clear it
        /// (WorldActions.Reach, through Authority.Near).
        public const float Reach = ReachRules.Reach;

        /// Whether this game sends the clear of a patch that started to
        /// fade just now. The host sends every one; a client only those its
        /// own player's swing made, since its copy of the host's fire
        /// clears what the host's does.
        public static bool SendsClear(in ClearFacts patch)
        {
            if (patch.WasFading || !patch.Fading || !patch.Sharing)
            {
                return false;
            }

            return !patch.ClientMode || patch.OwnSwing;
        }

        /// Either side: whether to clear one patch of an InfectionCleared.
        /// One not found, or fading already, is skipped. On the host, so is
        /// one further than the reach from the sender's stand-in; a sender
        /// with no stand-in yet reaches everything.
        public static bool Clears(in PatchFacts patch)
        {
            if (!patch.Found || patch.Fading)
            {
                return false;
            }

            if (!patch.Host)
            {
                return true;
            }

            return ReachRules.Near(patch.Reach with { Reach = Reach });
        }

        /// Host: the answer to a client's InfectionCleared of this many
        /// patches, of which this many were cleared. Refused only when
        /// there were some and none was cleared, whatever the reason; the
        /// rest of a partly cleared list is dropped without a word.
        public static Verdict MayClear(int asked, int cleared)
        {
            if (cleared == 0 && asked > 0)
            {
                return Verdict.Refuse(Refusal.InfectionOutOfReach);
            }

            return Verdict.Allow;
        }

        /// Host: whether the patches it cleared for a client go on to the
        /// others.
        public static bool Relays(int cleared)
        {
            return cleared > 0;
        }

        /// Whether Infection.spawnInfection runs. A client's infection
        /// never spreads: the host's new patches come as messages.
        public static bool MaySpread(bool clientMode)
        {
            return !clientMode;
        }

        /// Growing after a counted call ends: one less, never below 0.
        public static int Left(int growing)
        {
            if (growing > 0)
            {
                return growing - 1;
            }

            return growing;
        }

        /// Host: whether a patch that starts is sent as InfectionSpawned.
        /// Only the world's own new patches, the ones made inside a spread
        /// or a spider's egg.
        public static bool SendsSpawned(in SpawnFacts patch)
        {
            return patch.Growing > 0
                && patch.Exists
                && patch.Host
                && patch.Sharing;
        }

        /// Client: whether to make the host's new patch. Not when its id
        /// names a live object here already.
        public static bool MakesSpawned(bool known, bool alive)
        {
            return !(known && alive);
        }
    }
}
