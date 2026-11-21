using DarkwoodMP.World;
using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class OvenRulesTests
    {
        /// A joiner exactly at the reach of an oven at a hideout.
        static readonly OvenFacts AtHideout = new OvenFacts
        {
            Exists = true,
            Reach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(600f, 0f),
                Reach = ReachRules.Reach,
                What = "Oven",
            },
            AtLocation = true,
        };

        static readonly OvenFacts Far = AtHideout with
        {
            Reach = AtHideout.Reach with { Target = new Vec2(900f, 0f) },
        };

        /// A joiner's old oven, nobody else's.
        static readonly PutOutFacts Old = new PutOutFacts
        {
            HasOld = true,
            OldIsLit = false,
            OldIsHosts = false,
            OldIsOthers = false,
        };

        /// A refused oven that was off, still our player's.
        static readonly RollbackFacts Refused = new RollbackFacts
        {
            Allowed = false,
            Exists = true,
            WasOn = false,
            StillOurs = true,
        };

        /// The host lit an oven that is off here, and put out another.
        static readonly HostOvenFacts Lit = new HostOvenFacts
        {
            Found = true,
            On = false,
            HasOther = true,
            OtherIsLit = false,
            HasPlayer = true,
            OtherIsOurs = false,
        };

        static readonly OvenCallFacts Lighting = new OvenCallFacts
        {
            HasOven = true,
            Enable = true,
            Replaying = false,
            Together = true,
        };

        public class TakesOver
        {
            [Fact]
            public void Takes_over_a_lighting_together()
            {
                OvenRules.TakesOver(Lighting).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_call_that_doesnt_enable()
            {
                var call = Lighting with { Enable = false };

                OvenRules.TakesOver(call).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_call_with_no_oven()
            {
                var call = Lighting with { HasOven = false };

                OvenRules.TakesOver(call).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_replay()
            {
                var call = Lighting with { Replaying = true };

                OvenRules.TakesOver(call).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_game_alone()
            {
                var call = Lighting with { Together = false };

                OvenRules.TakesOver(call).ShouldBeFalse();
            }
        }

        public class MayLight
        {
            [Fact]
            public void Allows_an_oven_exactly_at_the_reach()
            {
                OvenRules.MayLight(AtHideout).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_oven_just_past_the_reach()
            {
                var oven = AtHideout with
                {
                    Reach = AtHideout.Reach with
                    {
                        Target = new Vec2(600.5f, 0f),
                    },
                };

                OvenRules.MayLight(oven).Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_as_the_host_does()
            {
                var text = Refusals.Text(OvenRules.MayLight(Far));

                text.ShouldBe("Oven is 900 away from them (reach 600)");
            }

            [Fact]
            public void Refuses_an_oven_not_here()
            {
                var oven = AtHideout with { Exists = false };

                OvenRules.MayLight(oven).Reason.ShouldBe(Refusal.NoSuchOven);
            }

            [Fact]
            public void Refuses_an_oven_not_at_a_location()
            {
                var oven = AtHideout with { AtLocation = false };

                var verdict = OvenRules.MayLight(oven);

                verdict.Reason.ShouldBe(Refusal.OvenNotAtHideout);
            }

            [Fact]
            public void Refuses_a_missing_oven_before_the_reach()
            {
                var oven = Far with { Exists = false };

                OvenRules.MayLight(oven).Reason.ShouldBe(Refusal.NoSuchOven);
            }

            [Fact]
            public void Refuses_the_reach_before_the_location()
            {
                var oven = Far with { AtLocation = false };

                OvenRules.MayLight(oven).Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Allows_a_requester_with_no_stand_in()
            {
                var oven = Far with
                {
                    Reach = Far.Reach with { Known = false },
                };

                OvenRules.MayLight(oven).ShouldBe(Verdict.Allow);
            }
        }

        public class PutsOut
        {
            [Fact]
            public void Puts_out_an_old_oven_nobody_has()
            {
                OvenRules.PutsOut(Old).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_hosts_own()
            {
                var old = Old with { OldIsHosts = true };

                OvenRules.PutsOut(old).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_another_joiners_own()
            {
                var old = Old with { OldIsOthers = true };

                OvenRules.PutsOut(old).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_oven_being_lit()
            {
                OvenRules.PutsOut(Old with { OldIsLit = true }).ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_with_no_old_oven()
            {
                OvenRules.PutsOut(Old with { HasOld = false }).ShouldBeFalse();
            }
        }

        public class Rollback
        {
            [Fact]
            public void Puts_out_and_gives_back_a_refused_oven()
            {
                var undo = OvenRules.Rollback(Refused);

                undo.PutOut.ShouldBeTrue();
                undo.GiveBack.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_lit_an_oven_that_was_on()
            {
                var undo = OvenRules.Rollback(Refused with { WasOn = true });

                undo.PutOut.ShouldBeFalse();
                undo.GiveBack.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_our_oven_if_it_changed_since()
            {
                var oven = Refused with { StillOurs = false };

                OvenRules.Rollback(oven).GiveBack.ShouldBeFalse();
            }

            [Fact]
            public void Undoes_nothing_when_allowed()
            {
                var oven = Refused with { Allowed = true };

                OvenRules.Rollback(oven).ShouldBe(default(OvenRollback));
            }

            [Fact]
            public void Undoes_nothing_for_an_oven_gone()
            {
                var oven = Refused with { Exists = false };

                OvenRules.Rollback(oven).ShouldBe(default(OvenRollback));
            }
        }

        public class FromHost
        {
            [Fact]
            public void Lights_it_and_puts_out_the_other()
            {
                var change = OvenRules.FromHost(Lit);

                change.Light.ShouldBeTrue();
                change.PutOut.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_oven_on_already()
            {
                var oven = Lit with { On = true };

                OvenRules.FromHost(oven).Light.ShouldBeFalse();
            }

            [Fact]
            public void Lights_nothing_not_found()
            {
                var oven = Lit with { Found = false };

                OvenRules.FromHost(oven).Light.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_our_own_oven()
            {
                var oven = Lit with { OtherIsOurs = true };

                OvenRules.FromHost(oven).PutOut.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_lit_one()
            {
                var oven = Lit with { OtherIsLit = true };

                OvenRules.FromHost(oven).PutOut.ShouldBeFalse();
            }

            [Fact]
            public void Puts_out_nothing_with_no_player()
            {
                var oven = Lit with { HasPlayer = false };

                OvenRules.FromHost(oven).PutOut.ShouldBeFalse();
            }

            [Fact]
            public void Puts_out_nothing_not_found()
            {
                var oven = Lit with { HasOther = false };

                OvenRules.FromHost(oven).PutOut.ShouldBeFalse();
            }
        }

        public class GivesHostsOven
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Gives_a_new_joiner_the_hosts_oven(
                bool host,
                bool player,
                bool gives)
            {
                OvenRules.GivesHostsOven(host, player).ShouldBe(gives);
            }
        }
    }
}
