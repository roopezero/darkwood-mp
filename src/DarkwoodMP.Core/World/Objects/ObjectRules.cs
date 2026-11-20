namespace DarkwoodMP.World.Objects
{
    /// Smaller world objects (the plugin's ObjectActions): the host's checks
    /// of a client's VineHit, ShadowArmorDied, Removed, LiquidLit and
    /// FeederUsed, and what each game sends of them or waits for.
    public static class ObjectRules
    {
        /// The most a player's swing takes off a vine in one hit
        /// (ObjectActions.VineDamageCap).
        public const float VineDamageCap = 1000f;

        /// How far from spilt gasoline a player may be to light it.
        public const float LightReach = 1500f;

        /// Host: whether a client's player could have cut at this vine: a
        /// vine, still attached with health left, a hit no larger than a
        /// swing's, and its root within a shot's reach (a vine is long: its
        /// root may be far from the cut), checked last. A hit that isn't a
        /// number passes the damage check.
        public static Verdict MayCutVine(in VineFacts vine)
        {
            if (!vine.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            if (!vine.IsVine)
            {
                return Verdict.Refuse(Refusal.NotAVine);
            }

            if (!vine.Attached || vine.Health <= 0f)
            {
                return Verdict.Refuse(Refusal.AlreadyCut);
            }

            if (vine.Damage < 0f || vine.Damage > VineDamageCap)
            {
                return Verdict.Refuse(
                    Refusal.HitTooLarge,
                    vine.Damage.ToString("0"));
            }

            return ReachRules.InReach(
                vine.Requester with { Reach = ReachRules.ShotReach });
        }

        /// Host: a client never says a shadow died; only the host does.
        /// One that isn't here is NotHere first. No reach check.
        public static Verdict MayKillShadow(bool exists)
        {
            if (!exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            return Verdict.Refuse(Refusal.HostDecidesShadows);
        }

        /// Host: whether a client's player could have taken this off the
        /// ground: a note or a death drop that is still here, not a lure,
        /// in reach (checked last).
        public static Verdict MayRemove(in PickupFacts thing)
        {
            if (!thing.Exists)
            {
                return Verdict.Refuse(Refusal.AlreadyGone);
            }

            if (thing.Lure)
            {
                return Verdict.Refuse(Refusal.AnimalsEatLures);
            }

            if (!thing.Note && !thing.DeathDrop)
            {
                return Verdict.Refuse(Refusal.CantPickUp);
            }

            return ReachRules.InReach(
                thing.Requester with { Reach = ReachRules.Reach });
        }

        /// Host: whether a client's player could have lit this: a liquid
        /// that burns, within LightReach.
        public static Verdict MayLight(in LiquidFacts liquid)
        {
            if (!liquid.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            if (!liquid.IsLiquid || !liquid.Flammable)
            {
                return Verdict.Refuse(Refusal.NotFlammable);
            }

            return ReachRules.InReach(
                liquid.Requester with { Reach = LightReach });
        }

        /// Host: whether a client's player could have used this feeder: a
        /// feeder, in reach, not used yet today.
        public static Verdict MayUseFeeder(in FeederFacts feeder)
        {
            if (!feeder.Exists)
            {
                return Verdict.Refuse(Refusal.NotHere);
            }

            if (!feeder.IsFeeder)
            {
                return Verdict.Refuse(Refusal.NotAFeeder);
            }

            var reach = ReachRules.InReach(
                feeder.Requester with { Reach = ReachRules.Reach });
            if (reach.Refused)
            {
                return reach;
            }

            if (!feeder.Active)
            {
                return Verdict.Refuse(Refusal.FeederUsed);
            }

            return Verdict.Allow;
        }

        /// Client, applying the host's VineHit: the vine's health before
        /// the hit, so that it ends at the health the host's hit left.
        public static float VineHealthBefore(float after, float damage)
        {
            return after + damage;
        }

        /// Whether a game tells the others that spilt gasoline caught fire
        /// (LiquidLitPatch): where a fire started, not a spread. A client
        /// tells only of a fire its own player's swing lit; the host's copy
        /// of a client's throw lights the rest there.
        public static bool SendsLit(in LitFacts lit)
        {
            if (!lit.WasOut || lit.Spreading || !lit.Burning)
            {
                return false;
            }

            if (!lit.Sharing)
            {
                return false;
            }

            return !lit.Client || lit.OwnSwing;
        }

        /// Whether Item.activate is watched for a pickup that is sent
        /// (PickupPatch): a note that is destroyed when read, or a death
        /// drop.
        public static bool WatchesPickup(bool note, bool kept, bool drop)
        {
            return (note && !kept) || drop;
        }

        /// What happens when a shadow dies here (ShadowArmorDiedPatch). Out
        /// of a session or a world, on a character, or from the save: as
        /// alone. A client's dies only as the host's replay.
        public static ShadowDeath OnShadowDied(in ShadowFacts shadow)
        {
            if (shadow.Instant || !shadow.OnItem)
            {
                return ShadowDeath.Dies;
            }

            if (!shadow.InSession || !shadow.InWorld)
            {
                return ShadowDeath.Dies;
            }

            if (shadow.Client && !shadow.Replaying)
            {
                return ShadowDeath.Waits;
            }

            if (shadow.Host)
            {
                return ShadowDeath.DiesForAll;
            }

            return ShadowDeath.Dies;
        }

        /// Client: whether its player's use of a feeder waits for the
        /// host's yes (FeederPatch). Not for a feeder that isn't shared or
        /// is ours alone.
        public static bool AsksForFeeder(in FeederAskFacts feeder)
        {
            if (!feeder.Sharing || !feeder.ClientMode)
            {
                return false;
            }

            return feeder.Named && !feeder.OursAlone;
        }

        /// Client: what to do with the host's answer to its player's use
        /// of a feeder. Nothing when the feeder or the player is gone, the
        /// "inactive" message on a refusal, else the feeder's activate.
        public static FeederOutcome OnFeederAnswer(in FeederAnswerFacts ans)
        {
            if (!ans.Here)
            {
                return FeederOutcome.Nothing;
            }

            if (!ans.Ok)
            {
                return FeederOutcome.Inactive;
            }

            return FeederOutcome.Activates;
        }
    }
}
