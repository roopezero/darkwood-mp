using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class SkillResetRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_hosts_name_in_the_record()
            {
                SkillResetRules.HostWho.ShouldBe("host");
            }

            [Fact]
            public void Pins_32_hex_characters_a_client()
            {
                SkillResetRules.WhoLength.ShouldBe(32);
            }
        }

        public class Who
        {
            [Theory]
            [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223")]
            [InlineData("", "e3b0c44298fc1c149afbf4c8996fb924")]
            [InlineData(null, "e3b0c44298fc1c149afbf4c8996fb924")]
            [InlineData("pelaaja-ä", "eecfa8471c3eb72dc0f309ee6dd84a37")]
            public void Names_a_client_by_the_hash_of_its_key(
                string key,
                string who)
            {
                var name = SkillResetRules.Who(key);

                name.ShouldBe(who);
            }
        }

        public class Alone
        {
            [Theory]
            [InlineData("host", true)]
            [InlineData("ba7816bf8f01cfea414140de5dae2223", false)]
            public void Starts_alone_only_for_our_own_player(
                string who,
                bool alone)
            {
                var starts = SkillResetRules.Alone(who);

                starts.ShouldBe(alone);
            }
        }

        public class Morning
        {
            [Fact]
            public void Allows_a_reset_in_the_morning()
            {
                var verdict = SkillResetRules.Morning(true);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_reset_later_in_the_day()
            {
                var verdict = SkillResetRules.Morning(false);

                verdict.Reason.ShouldBe(Refusal.NotMorning);
            }
        }

        public class Today
        {
            static readonly ResetFacts Fresh = new ResetFacts
            {
                HasRecord = true,
                Today = 4,
            };

            [Fact]
            public void Allows_a_first_reset()
            {
                var verdict = SkillResetRules.Today(Fresh);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_reset_on_a_later_day()
            {
                var verdict = SkillResetRules.Today(
                    Fresh with { HasDay = true, Day = 3 });

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_second_reset_the_same_day()
            {
                var verdict = SkillResetRules.Today(
                    Fresh with { HasDay = true, Day = 4 });

                verdict.Reason.ShouldBe(Refusal.AlreadyReset);
            }

            [Fact]
            public void Refuses_a_reset_with_nobody_sharing()
            {
                var verdict = SkillResetRules.Today(
                    Fresh with { HasRecord = false });

                verdict.Reason.ShouldBe(Refusal.NoSharedProgression);
            }

            [Fact]
            public void Refuses_nobody_sharing_before_a_reset_today()
            {
                var reset = Fresh with
                {
                    HasRecord = false,
                    HasDay = true,
                    Day = 4,
                };

                var verdict = SkillResetRules.Today(reset);

                verdict.Reason.ShouldBe(Refusal.NoSharedProgression);
            }

            [Fact]
            public void Allows_day_0_when_no_day_is_recorded()
            {
                var verdict = SkillResetRules.Today(Fresh with { Today = 0 });

                verdict.Refused.ShouldBeFalse();
            }
        }

        public class Handles
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Handles_a_reset_as_the_host_from_a_known_player(
                bool isHost,
                bool hasKey,
                bool handles)
            {
                var looks = SkillResetRules.Handles(isHost, hasKey);

                looks.ShouldBe(handles);
            }
        }

        public class CanResetHere
        {
            static readonly StoveFacts AtStove = new StoveFacts
            {
                HasPlayer = true,
                InLevelingMenu = true,
                HasMenu = true,
                OpenedAndTweened = true,
            };

            [Fact]
            public void Allows_it_at_the_stoves_still_menu()
            {
                var here = SkillResetRules.CanResetHere(AtStove);

                here.ShouldBeTrue();
            }

            public static TheoryData<StoveFacts> Elsewhere =>
                new TheoryData<StoveFacts>
                {
                    AtStove with { HasPlayer = false },
                    AtStove with { InLevelingMenu = false },
                    AtStove with { HasMenu = false },
                    AtStove with { OpenedAndTweened = false },
                    AtStove with { WantToLevel = true },
                    AtStove with { SkillsMenuOpen = true },
                };

            [Theory]
            [MemberData(nameof(Elsewhere))]
            public void Refuses_it_unless_it_all_holds(StoveFacts stove)
            {
                var here = SkillResetRules.CanResetHere(stove);

                here.ShouldBeFalse();
            }
        }

        public class Asks
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Asks_unless_an_answer_is_waiting(
                bool waiting,
                bool asks)
            {
                var ask = SkillResetRules.Asks(waiting);

                ask.ShouldBe(asks);
            }
        }
    }
}
