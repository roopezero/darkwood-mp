using DarkwoodMP.World;
using DarkwoodMP.World.Switches;

namespace DarkwoodMP.Tests.World.Switches
{
    public class SwitchRulesTests
    {
        /// A switch that is off, which the client switched on, exactly at
        /// the reach.
        static readonly SwitchFacts Off = new SwitchFacts
        {
            Exists = true,
            IsSwitch = true,
            On = true,
            Requester = Requesters.AtReach,
        };

        static readonly ReachFacts Far = Requesters.PastReach;

        public class MayTurn
        {
            [Fact]
            public void Allows_an_item_in_reach()
            {
                var verdict = SwitchRules.MayTurn(Off);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_an_item_already_as_asked()
            {
                var item = Off with { IsSwitch = false, IsOn = true };

                var verdict = SwitchRules.MayTurn(item);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_here()
            {
                var item = Off with { Exists = false };

                var verdict = SwitchRules.MayTurn(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_an_item_just_past_the_reach()
            {
                var item = Off with { Requester = Far };

                var verdict = SwitchRules.MayTurn(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class MaySwitch
        {
            [Fact]
            public void Allows_a_switch_turned_on()
            {
                var verdict = SwitchRules.MaySwitch(Off);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_switch_turned_off()
            {
                var item = Off with { IsOn = true, On = false };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_switch_that_isnt_here()
            {
                var item = Off with { Exists = false };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.Reason.ShouldBe(Refusal.NotHere);
            }

            [Fact]
            public void Refuses_what_isnt_a_switch()
            {
                var item = Off with { IsSwitch = false, Requester = Far };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.Reason.ShouldBe(Refusal.NotASwitch);
            }

            [Fact]
            public void Refuses_a_switch_just_past_the_reach()
            {
                var item = Off with { Requester = Far, IsOn = true };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_switch_already_on()
            {
                var item = Off with { IsOn = true };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.Reason.ShouldBe(Refusal.AlreadyOn);
            }

            [Fact]
            public void Refuses_a_switch_already_off()
            {
                var item = Off with { On = false };

                var verdict = SwitchRules.MaySwitch(item);

                verdict.Reason.ShouldBe(Refusal.AlreadyOff);
            }
        }
    }
}
