using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class EndingRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_waits_for_the_epilogue()
            {
                var limits = new[]
                {
                    EndingRules.TryFor,
                    EndingRules.EnterFor,
                    EndingRules.AloneEnterFor,
                };

                limits.ShouldBe(new[] { 30f, 60f, 45f });
            }
        }

        public class HostEnds
        {
            [Theory]
            [InlineData(EndKind.Epilogue)]
            [InlineData(EndKind.EndScreen)]
            public void Ends_the_game_on_the_host_once(EndKind kind)
            {
                EndingRules.HostEnds(true, false, kind).ShouldBe(kind);
            }

            [Theory]
            [InlineData(false, false)]
            [InlineData(true, true)]
            public void Ends_nothing_otherwise(bool host, bool sent)
            {
                EndingRules.HostEnds(host, sent, EndKind.Epilogue)
                    .ShouldBe(EndKind.None);
            }
        }

        public class DreamEnds
        {
            [Theory]
            [InlineData("epilog_part1a_dream", true)]
            [InlineData("epilog_part2_dream", false)]
            [InlineData(null, false)]
            public void Ends_the_game_only_with_the_epilogues_dream(
                string preset,
                bool ends)
            {
                EndingRules.DreamEnds(preset).ShouldBe(ends);
            }
        }

        public class GameEnd
        {
            /// The host's credits, while playing the epilogue in a world.
            static readonly GameEndFacts Credits = new GameEndFacts
            {
                FromHost = true,
                Kind = 1,
                Phase = 1,
                Playing = true,
                InWorld = true,
            };

            [Fact]
            public void Leaves_for_the_credits_from_the_epilogue()
            {
                EndingRules.GameEnd(Credits)
                    .ShouldBe(GameEndStep.LeaveToCredits);
            }

            [Fact]
            public void Leaves_for_the_credits_from_the_epilogue_alone()
            {
                var facts = Credits with { InWorld = false, InEpilogue = true };

                EndingRules.GameEnd(facts)
                    .ShouldBe(GameEndStep.LeaveToCredits);
            }

            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Only_leaves_at_the_credits_or_out_of_a_world(
                bool playing,
                bool credits,
                bool world)
            {
                var facts = Credits with
                {
                    Playing = playing,
                    AtCredits = credits,
                    InWorld = world,
                };

                EndingRules.GameEnd(facts).ShouldBe(GameEndStep.Leave);
            }

            [Fact]
            public void Joins_the_epilogue_at_its_start_in_a_world()
            {
                var facts = Credits with { Phase = 0 };

                EndingRules.GameEnd(facts)
                    .ShouldBe(GameEndStep.JoinEpilogue);
            }

            [Fact]
            public void Only_begins_the_epilogue_out_of_a_world()
            {
                var facts = Credits with { Phase = 0, InWorld = false };

                EndingRules.GameEnd(facts)
                    .ShouldBe(GameEndStep.BeginEpilogue);
            }

            [Theory]
            [InlineData(2, 1)]
            [InlineData(2, 0)]
            [InlineData(0, 0)]
            public void Ends_any_other_kind_alone(byte kind, byte phase)
            {
                var facts = Credits with { Kind = kind, Phase = phase };

                EndingRules.GameEnd(facts).ShouldBe(GameEndStep.Solo);
            }

            [Fact]
            public void Ignores_it_from_anyone_but_the_host()
            {
                var facts = Credits with { FromHost = false };

                EndingRules.GameEnd(facts).ShouldBe(GameEndStep.Ignored);
            }
        }

        public class ShowsEnding
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Shows_it_in_a_world_with_saves_redirected(
                bool world,
                bool redirected,
                bool shows)
            {
                EndingRules.ShowsEnding(world, redirected).ShouldBe(shows);
            }
        }

        public class MayStartAlone
        {
            static readonly StartFacts Awake = new StartFacts
            {
                Dreams = new DreamFacts { Exists = true },
                HasPlayer = true,
            };

            [Fact]
            public void Starts_awake_in_the_world()
            {
                EndingRules.MayStartAlone(Awake).ShouldBeTrue();
            }

            [Fact]
            public void Starts_while_loading_or_near_a_dream()
            {
                var facts = Awake with
                {
                    Loading = true,
                    PlaceLoading = true,
                    Dreams = Awake.Dreams with
                    {
                        Prepared = true,
                        Switching = true,
                    },
                };

                EndingRules.MayStartAlone(facts).ShouldBeTrue();
            }

            [Fact]
            public void Doesnt_start_while_dreaming()
            {
                var facts = Awake with
                {
                    Dreams = Awake.Dreams with { Dreaming = true },
                };

                EndingRules.MayStartAlone(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_in_an_outside_location()
            {
                var facts = Awake with { InOutside = true };

                EndingRules.MayStartAlone(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_with_no_player()
            {
                var facts = Awake with { HasPlayer = false };

                EndingRules.MayStartAlone(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_with_no_dreams()
            {
                var facts = Awake with { Dreams = default };

                EndingRules.MayStartAlone(facts).ShouldBeFalse();
            }
        }

        public class MayStartShared
        {
            static readonly StartFacts Awake = new StartFacts
            {
                Dreams = new DreamFacts { Exists = true },
                HasPlayer = true,
            };

            [Fact]
            public void Starts_awake_even_in_an_outside_location()
            {
                var facts = Awake with { InOutside = true };

                EndingRules.MayStartShared(facts).ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            [InlineData(false, false, true)]
            public void Doesnt_start_in_or_near_a_dream(
                bool dreaming,
                bool prepared,
                bool switching)
            {
                var facts = Awake with
                {
                    Dreams = Awake.Dreams with
                    {
                        Dreaming = dreaming,
                        Prepared = prepared,
                        Switching = switching,
                    },
                };

                EndingRules.MayStartShared(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_while_the_game_loads()
            {
                var facts = Awake with { Loading = true };

                EndingRules.MayStartShared(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_while_a_place_loads()
            {
                var facts = Awake with { PlaceLoading = true };

                EndingRules.MayStartShared(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_with_no_player()
            {
                var facts = Awake with { HasPlayer = false };

                EndingRules.MayStartShared(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_start_with_no_dreams()
            {
                var facts = Awake with { Dreams = default };

                EndingRules.MayStartShared(facts).ShouldBeFalse();
            }
        }

        public class JoinEnds
        {
            [Theory]
            [InlineData(true, true, JoinEnd.Joined)]
            [InlineData(false, true, JoinEnd.Joined)]
            [InlineData(false, false, JoinEnd.Stopped)]
            [InlineData(true, false, JoinEnd.Fallback)]
            public void Ends_the_way_into_the_epilogue(
                bool playing,
                bool inEpilogue,
                JoinEnd end)
            {
                EndingRules.JoinEnds(playing, inEpilogue).ShouldBe(end);
            }
        }

        public class AfterEnding
        {
            [Fact]
            public void Is_done_at_the_main_menu()
            {
                EndingRules.AfterEnding(false, true, new Seconds(99f))
                    .ShouldBe(EndingWait.AtMenu);
            }

            [Fact]
            public void Waits_for_the_credits_a_while()
            {
                EndingRules.AfterEnding(false, false, new Seconds(10f))
                    .ShouldBe(EndingWait.Waiting);
            }

            [Fact]
            public void Loads_the_menu_when_the_credits_never_came()
            {
                var waited = new Seconds(EndingRules.CreditsWithin + 1f);

                EndingRules.AfterEnding(false, false, waited)
                    .ShouldBe(EndingWait.ForceMenu);
            }

            [Fact]
            public void Leaves_rolling_credits_to_end_by_themselves()
            {
                EndingRules.AfterEnding(true, false, new Seconds(300f))
                    .ShouldBe(EndingWait.Waiting);
            }
        }

        public class CreditsWhenHostGone
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            public void Goes_to_the_credits_only_mid_epilogue_on_a_joiner(
                bool playing,
                bool client,
                bool atCredits,
                bool goes)
            {
                EndingRules.CreditsWhenHostGone(playing, client, atCredits)
                    .ShouldBe(goes);
            }
        }

        public class LeavesForMenu
        {
            [Fact]
            public void Leaves_the_world_when_no_ending_is_on()
            {
                EndingRules.LeavesForMenu(false).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_to_the_ending_through_the_credits()
            {
                EndingRules.LeavesForMenu(true).ShouldBeFalse();
            }
        }
    }
}
