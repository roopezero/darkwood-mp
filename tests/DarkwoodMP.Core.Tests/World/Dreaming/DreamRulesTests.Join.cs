using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public partial class DreamRulesTests
    {
        public class Join
        {
            static readonly JoinFacts Ready = new JoinFacts
            {
                ClientMode = true,
                InWorld = true,
                HasDreams = true,
                HasPlayer = true,
                Now = T(10f),
                StartBy = T(70f),
            };

            [Fact]
            public void Goes_in_when_nothing_holds_it()
            {
                var step = DreamRules.Join(Ready);

                step.Action.ShouldBe(JoinAction.Go);
            }

            public static TheoryData<JoinFacts> Dropped =>
                new TheoryData<JoinFacts>
                {
                    Ready with { ClientMode = false },
                    Ready with { InWorld = false },
                    Ready with { HasDreams = false },
                    Ready with { HasPlayer = false },
                    Ready with { InDream = true },
                    Ready with { InDream = true, Now = T(71f) },
                };

            [Theory]
            [MemberData(nameof(Dropped))]
            public void Drops_it_when_it_cant_go(JoinFacts join)
            {
                var step = DreamRules.Join(join);

                step.Action.ShouldBe(JoinAction.Drop);
            }

            [Fact]
            public void Keeps_trying_at_the_give_up_time()
            {
                var step = DreamRules.Join(Ready with { Now = T(70f) });

                step.Action.ShouldBe(JoinAction.Go);
            }

            [Fact]
            public void Gives_up_just_past_it()
            {
                var step = DreamRules.Join(Ready with { Now = T(70.01f) });

                step.Action.ShouldBe(JoinAction.GiveUp);
            }

            [Fact]
            public void Gives_up_before_waiting_for_a_menu()
            {
                var join = Ready with { Now = T(71f), InDialogue = true };

                var step = DreamRules.Join(join);

                step.Action.ShouldBe(JoinAction.GiveUp);
            }

            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Waits_for_the_skill_choice_or_a_talk(
                bool skills,
                bool talk)
            {
                var join = Ready with
                {
                    SkillsMenuOpen = skills,
                    InDialogue = talk,
                    AtStove = true,
                };

                var step = DreamRules.Join(join);

                step.Action.ShouldBe(JoinAction.Wait);
            }

            [Fact]
            public void Waits_a_moment_after_the_stove()
            {
                var step = DreamRules.Join(Ready with { AtStove = true });

                step.Action.ShouldBe(JoinAction.Stove);
            }

            [Fact]
            public void Starts_the_wait_after_the_stove_from_now()
            {
                var step = DreamRules.Join(Ready with { AtStove = true });

                step.StartAt.Value.ShouldBe(10.7f, 0.0001f);
            }

            [Fact]
            public void Waits_until_the_start_time()
            {
                var step = DreamRules.Join(Ready with { StartAt = T(10.5f) });

                step.Action.ShouldBe(JoinAction.Wait);
            }

            [Fact]
            public void Goes_at_the_start_time()
            {
                var step = DreamRules.Join(Ready with { StartAt = T(10f) });

                step.Action.ShouldBe(JoinAction.Go);
            }

            [Fact]
            public void Keeps_the_start_time_when_not_at_the_stove()
            {
                var step = DreamRules.Join(Ready with { StartAt = T(10.5f) });

                step.StartAt.Value.ShouldBe(10.5f);
            }
        }

        public class InputsFrom
        {
            [Fact]
            public void Begins_six_seconds_after_the_end()
            {
                var from = DreamRules.InputsFrom(T(20f));

                from.Value.ShouldBe(26f);
            }
        }

        public class Inputs
        {
            static readonly InputFacts Held = new InputFacts
            {
                Now = T(30f),
                From = T(26f),
                InWorld = true,
                ForbidInputs = true,
                HeldByNothing = true,
            };

            [Fact]
            public void Lets_go_of_a_player_nothing_holds()
            {
                var step = DreamRules.Inputs(Held);

                step.ShouldBe(InputStep.LetGo);
            }

            [Theory]
            [InlineData(0f, 30f)]
            [InlineData(26f, 26f)]
            [InlineData(26f, 25f)]
            public void Doesnt_begin_before_its_time(float from, float now)
            {
                var step = DreamRules.Inputs(
                    Held with { From = T(from), Now = T(now) });

                step.ShouldBe(InputStep.NotYet);
            }

            [Fact]
            public void Keeps_checking_at_the_give_up_time()
            {
                var step = DreamRules.Inputs(Held with { Now = T(66f) });

                step.ShouldBe(InputStep.LetGo);
            }

            [Fact]
            public void Gives_up_just_past_it()
            {
                var step = DreamRules.Inputs(Held with { Now = T(66.01f) });

                step.ShouldBe(InputStep.Stop);
            }

            [Fact]
            public void Stops_out_of_the_world()
            {
                var step = DreamRules.Inputs(Held with { InWorld = false });

                step.ShouldBe(InputStep.Stop);
            }

            [Fact]
            public void Stops_once_inputs_are_free()
            {
                var step = DreamRules.Inputs(
                    Held with { ForbidInputs = false, HeldByNothing = false });

                step.ShouldBe(InputStep.Stop);
            }

            [Fact]
            public void Waits_while_something_holds_the_player()
            {
                var step = DreamRules.Inputs(
                    Held with { HeldByNothing = false });

                step.ShouldBe(InputStep.Wait);
            }
        }

        public class HeldByNothing
        {
            static readonly HoldFacts Free = new HoldFacts { HasPlayer = true };

            [Fact]
            public void Finds_nothing_holding_a_free_player()
            {
                var free = DreamRules.HeldByNothing(Free);

                free.ShouldBeTrue();
            }

            public static TheoryData<HoldFacts> Holding =>
                new TheoryData<HoldFacts>
                {
                    Free with { HasPlayer = false },
                    Free with { Locked = true },
                    Free with { LoadingGame = true },
                    Free with { Cutscene = true },
                    Free with { OutsideLoading = true },
                    Free with { Switching = true },
                    Free with { EnteringDream = true },
                    Free with { InDialogue = true },
                };

            [Theory]
            [MemberData(nameof(Holding))]
            public void Finds_what_holds_the_player(HoldFacts hold)
            {
                var free = DreamRules.HeldByNothing(hold);

                free.ShouldBeFalse();
            }
        }

        public class Over
        {
            [Fact]
            public void Is_over_once_awake()
            {
                var over = DreamRules.Over(Awake, Shared);

                over.ShouldBeTrue();
            }

            public static TheoryData<DreamFacts, DreamShareFacts> NotOver =>
                new TheoryData<DreamFacts, DreamShareFacts>
                {
                    { Awake, Shared with { Shared = false } },
                    { new DreamFacts(), Shared },
                    { Dreaming, Shared },
                    { Awake with { Switching = true }, Shared },
                    { Awake, Shared with { EpiloguePlaying = true } },
                };

            [Theory]
            [MemberData(nameof(NotOver))]
            public void Isnt_over_while_it_goes_on(
                DreamFacts dream,
                DreamShareFacts share)
            {
                var over = DreamRules.Over(dream, share);

                over.ShouldBeFalse();
            }
        }
    }
}
