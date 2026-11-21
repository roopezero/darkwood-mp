using DarkwoodMP.World;
using DarkwoodMP.World.Objects;

namespace DarkwoodMP.Tests.World.Objects
{
    public partial class ObjectRulesTests
    {
        static readonly ReachFacts AtReach = Requesters.AtReach;

        static readonly ReachFacts Far = Requesters.PastReach;

        public class Limits
        {
            [Fact]
            public void Pins_the_vine_and_light_limits()
            {
                var limits = new[]
                {
                    ObjectRules.VineDamageCap,
                    ObjectRules.LightReach,
                };

                limits.ShouldBe(new[] { 1000f, 1500f });
            }
        }

        public class MayCutVine
        {
            /// An attached vine cut at with the biggest hit, its root
            /// exactly at a shot's reach.
            static readonly VineFacts Vine = new VineFacts
            {
                Exists = true,
                IsVine = true,
                Attached = true,
                Health = 50f,
                Damage = 1000f,
                Requester = Requesters.AtShotReach,
            };

            [Fact]
            public void Allows_the_biggest_hit_at_a_shot_reach()
            {
                var verdict = ObjectRules.MayCutVine(Vine);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_vine_that_isnt_here()
            {
                var vine = Vine with { Exists = false };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_what_isnt_a_vine()
            {
                var vine = Vine with { IsVine = false };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.NotAVine);
            }

            [Fact]
            public void Refuses_a_vine_that_isnt_attached()
            {
                var vine = Vine with { Attached = false };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.AlreadyCut);
            }

            [Fact]
            public void Refuses_a_vine_with_no_health_left()
            {
                var vine = Vine with { Health = 0f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.AlreadyCut);
            }

            [Fact]
            public void Refuses_a_hit_just_past_the_cap()
            {
                var vine = Vine with { Damage = 1000.5f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Allows_a_hit_of_zero()
            {
                var vine = Vine with { Damage = 0f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_a_cut_vine_before_the_hit()
            {
                var vine = Vine with { Attached = false, Damage = 1500f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.AlreadyCut);
            }

            [Fact]
            public void Refuses_a_negative_hit()
            {
                var vine = Vine with { Damage = -0.5f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }

            [Fact]
            public void Allows_a_hit_that_isnt_a_number()
            {
                var vine = Vine with { Damage = float.NaN };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Names_the_hit_in_the_refusal()
            {
                var vine = Vine with { Damage = 1500f };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Detail.ShouldBe("1500");
            }

            [Fact]
            public void Refuses_a_root_just_past_a_shot_reach()
            {
                var vine = Vine with { Requester = Requesters.PastShotReach };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_the_reach_last()
            {
                var vine = Vine with
                {
                    Damage = 2000f,
                    Requester = Requesters.At(5000f),
                };

                var verdict = ObjectRules.MayCutVine(vine);

                verdict.Reason.ShouldBe(Refusal.HitTooLarge);
            }
        }

        public class MayKillShadow
        {
            [Fact]
            public void Refuses_a_shadow_a_client_says_died()
            {
                var verdict = ObjectRules.MayKillShadow(true);

                verdict.Reason.ShouldBe(Refusal.HostDecidesShadows);
            }

            [Fact]
            public void Refuses_a_shadow_that_isnt_here()
            {
                var verdict = ObjectRules.MayKillShadow(false);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }
        }

        public class MayRemove
        {
            /// A note lying exactly at the reach.
            static readonly PickupFacts Note = new PickupFacts
            {
                Exists = true,
                Note = true,
                Requester = AtReach,
            };

            [Fact]
            public void Allows_a_note_in_reach()
            {
                var verdict = ObjectRules.MayRemove(Note);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_death_drop()
            {
                var thing = Note with { Note = false, DeathDrop = true };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_thing_already_gone()
            {
                var thing = Note with { Exists = false };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.AlreadyGone);
            }

            [Fact]
            public void Refuses_a_lure()
            {
                var thing = Note with { Lure = true };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.AnimalsEatLures);
            }

            [Fact]
            public void Refuses_what_cant_be_picked_up()
            {
                var thing = Note with { Note = false };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.CantPickUp);
            }

            [Fact]
            public void Refuses_a_note_just_past_the_reach()
            {
                var thing = Note with { Requester = Far };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_the_reach_last()
            {
                var thing = Note with { Requester = Far, Lure = true };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.AnimalsEatLures);
            }

            [Fact]
            public void Puts_a_lure_before_what_cant_be_picked_up()
            {
                var thing = Note with { Note = false, Lure = true };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.AnimalsEatLures);
            }

            [Fact]
            public void Puts_the_reach_after_what_cant_be_picked_up()
            {
                var thing = Note with { Note = false, Requester = Far };

                var verdict = ObjectRules.MayRemove(thing);

                verdict.Reason.ShouldBe(Refusal.CantPickUp);
            }
        }

        public class MayLight
        {
            /// Gasoline exactly at the light reach.
            static readonly LiquidFacts Gasoline = new LiquidFacts
            {
                Exists = true,
                IsLiquid = true,
                Flammable = true,
                Requester = Requesters.At(ObjectRules.LightReach),
            };

            [Fact]
            public void Allows_gasoline_at_the_light_reach()
            {
                var verdict = ObjectRules.MayLight(Gasoline);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_liquid_that_isnt_here()
            {
                var liquid = Gasoline with { Exists = false };

                var verdict = ObjectRules.MayLight(liquid);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_what_isnt_a_liquid()
            {
                var liquid = Gasoline with { IsLiquid = false };

                var verdict = ObjectRules.MayLight(liquid);

                verdict.Reason.ShouldBe(Refusal.NotFlammable);
            }

            [Fact]
            public void Refuses_a_liquid_that_doesnt_burn()
            {
                var liquid = Gasoline with { Flammable = false };

                var verdict = ObjectRules.MayLight(liquid);

                verdict.Reason.ShouldBe(Refusal.NotFlammable);
            }

            [Fact]
            public void Refuses_gasoline_just_past_the_light_reach()
            {
                var liquid = Gasoline with
                {
                    Requester = Requesters.At(ObjectRules.LightReach + 0.5f),
                };

                var verdict = ObjectRules.MayLight(liquid);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_the_reach_last()
            {
                var liquid = Gasoline with
                {
                    Flammable = false,
                    Requester = Requesters.At(ObjectRules.LightReach + 0.5f),
                };

                var verdict = ObjectRules.MayLight(liquid);

                verdict.Reason.ShouldBe(Refusal.NotFlammable);
            }
        }

        public class MayUseFeeder
        {
            /// A feeder not used today, exactly at the reach.
            static readonly FeederFacts Feeder = new FeederFacts
            {
                Exists = true,
                IsFeeder = true,
                Active = true,
                Requester = AtReach,
            };

            [Fact]
            public void Allows_a_feeder_not_used_today()
            {
                var verdict = ObjectRules.MayUseFeeder(Feeder);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_feeder_that_isnt_here()
            {
                var feeder = Feeder with { Exists = false };

                var verdict = ObjectRules.MayUseFeeder(feeder);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_what_isnt_a_feeder()
            {
                var feeder = Feeder with { IsFeeder = false, Requester = Far };

                var verdict = ObjectRules.MayUseFeeder(feeder);

                verdict.Reason.ShouldBe(Refusal.NotAFeeder);
            }

            [Fact]
            public void Refuses_a_feeder_just_past_the_reach()
            {
                var feeder = Feeder with { Requester = Far, Active = false };

                var verdict = ObjectRules.MayUseFeeder(feeder);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_feeder_used_today()
            {
                var feeder = Feeder with { Active = false };

                var verdict = ObjectRules.MayUseFeeder(feeder);

                verdict.Reason.ShouldBe(Refusal.FeederUsed);
            }
        }
    }
}
