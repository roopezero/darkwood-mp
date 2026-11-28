using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class EpilogueRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_pans_timeout()
            {
                EpilogueRules.PanWait.ShouldBe(60f);
            }

            [Fact]
            public void Pins_the_epilogues_names()
            {
                var names = new[]
                {
                    EpilogueRules.PanEvent,
                    EpilogueRules.BurnEvents,
                    EpilogueRules.DreamPrefix,
                };

                names.ShouldBe(new[]
                {
                    "epilogue_cameraPanOverBurningForest",
                    "Istota_lv1_events",
                    "epilog_",
                });
            }
        }

        public class InEpilogueAlone
        {
            static readonly DreamFacts Epilogue = new DreamFacts
            {
                Exists = true,
                Dreaming = true,
                HasPreset = true,
                Preset = "epilog_part2_dream",
            };

            [Fact]
            public void Finds_a_world_dreaming_an_epilogue()
            {
                EpilogueRules.InEpilogueAlone(false, false, Epilogue)
                    .ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Finds_an_epilogue_dream_wanted_or_prepared(
                bool wanted,
                bool prepared)
            {
                var dreams = Epilogue with
                {
                    Dreaming = false,
                    Wanted = wanted,
                    Prepared = prepared,
                };

                EpilogueRules.InEpilogueAlone(false, false, dreams)
                    .ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_dream_only_switching()
            {
                var dreams = Epilogue with
                {
                    Dreaming = false,
                    Switching = true,
                };

                EpilogueRules.InEpilogueAlone(false, false, dreams)
                    .ShouldBeFalse();
            }

            [Fact]
            public void Finds_a_player_in_the_epilogue_with_no_dreams()
            {
                EpilogueRules.InEpilogueAlone(false, true, default)
                    .ShouldBeTrue();
            }

            [Fact]
            public void Never_counts_the_epilogue_played_together()
            {
                EpilogueRules.InEpilogueAlone(true, true, Epilogue)
                    .ShouldBeFalse();
            }

            [Fact]
            public void Ignores_another_dream()
            {
                var dreams = Epilogue with { Preset = "dream_tutorial_00" };

                EpilogueRules.InEpilogueAlone(false, false, dreams)
                    .ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_preset_with_no_name()
            {
                var dreams = Epilogue with { Preset = null };

                EpilogueRules.InEpilogueAlone(false, false, dreams)
                    .ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_dream_with_no_preset()
            {
                var dreams = Epilogue with { HasPreset = false };

                EpilogueRules.InEpilogueAlone(false, false, dreams)
                    .ShouldBeFalse();
            }
        }

        public class JoinRefusal
        {
            [Fact]
            public void Refuses_a_joiner_while_the_host_plays_the_ending()
            {
                var verdict = EpilogueRules.JoinRefusal(true, true);

                verdict.Reason.ShouldBe(Refusal.PlayingEnding);
                Refusals.Text(verdict)
                    .ShouldBe("The host is playing the ending.");
            }

            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Allows_a_joiner_otherwise(bool playing, bool host)
            {
                EpilogueRules.JoinRefusal(playing, host)
                    .ShouldBe(Verdict.Allow);
            }
        }

        public class OutcomeAllowed
        {
            static readonly string[] Outcomes = { "sleep", "burn" };

            [Fact]
            public void Allows_one_of_the_presets_outcomes()
            {
                EpilogueRules.OutcomeAllowed(true, Outcomes, "burn")
                    .ShouldBeTrue();
            }

            [Fact]
            public void Refuses_another_outcome_while_playing()
            {
                EpilogueRules.OutcomeAllowed(true, Outcomes, "wake")
                    .ShouldBeFalse();
            }

            [Fact]
            public void Allows_any_outcome_when_not_playing()
            {
                EpilogueRules.OutcomeAllowed(false, Outcomes, "wake")
                    .ShouldBeTrue();
            }

            [Fact]
            public void Allows_any_outcome_with_no_preset()
            {
                EpilogueRules.OutcomeAllowed(true, null, "wake")
                    .ShouldBeTrue();
            }
        }

        public class BeforeCredits
        {
            [Theory]
            [InlineData(true, true, true, CreditsStep.HostStops)]
            [InlineData(true, true, false, CreditsStep.JoinerLeaves)]
            [InlineData(false, true, true, CreditsStep.None)]
            [InlineData(true, false, true, CreditsStep.None)]
            public void Ends_the_session_at_the_credits_of_the_epilogue(
                bool session,
                bool playing,
                bool host,
                CreditsStep step)
            {
                EpilogueRules.BeforeCredits(session, playing, host)
                    .ShouldBe(step);
            }
        }

        public class WorldEvent
        {
            /// The pan's event while playing, before its release.
            static readonly PanEventFacts Pan = new PanEventFacts
            {
                Playing = true,
                IsPan = true,
                InSession = true,
            };

            [Fact]
            public void Holds_the_pan_until_it_is_released()
            {
                EpilogueRules.WorldEvent(Pan).ShouldBe(PanEventStep.Waits);
            }

            [Fact]
            public void Runs_the_first_pan_after_the_release()
            {
                var facts = Pan with { Released = true };

                EpilogueRules.WorldEvent(facts)
                    .ShouldBe(PanEventStep.RunsFirst);
            }

            [Fact]
            public void Runs_no_second_pan()
            {
                var facts = Pan with { Released = true, PanRan = true };

                EpilogueRules.WorldEvent(facts).ShouldBe(PanEventStep.Held);
            }

            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Runs_any_other_event(
                bool playing,
                bool pan,
                bool session)
            {
                var facts = Pan with
                {
                    Playing = playing,
                    IsPan = pan,
                    InSession = session,
                };

                EpilogueRules.WorldEvent(facts).ShouldBe(PanEventStep.Runs);
            }
        }

        public class CountsDeath
        {
            static readonly DeathFacts Crawl = new DeathFacts
            {
                Playing = true,
                InSession = true,
                HasPlayer = true,
                InEpilogue = true,
                Crawling = true,
                Dying = true,
            };

            [Fact]
            public void Counts_the_death_at_the_end_of_the_crawl()
            {
                EpilogueRules.CountsDeath(Crawl).ShouldBeTrue();
            }

            [Fact]
            public void Counts_it_once()
            {
                var facts = Crawl with { Counted = true };

                EpilogueRules.CountsDeath(facts).ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, true, true, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, true, false, true)]
            [InlineData(true, true, true, false)]
            public void Needs_the_crawl_in_the_epilogue(
                bool playing,
                bool inEpilogue,
                bool crawling,
                bool dying)
            {
                var facts = Crawl with
                {
                    Playing = playing,
                    InEpilogue = inEpilogue,
                    Crawling = crawling,
                    Dying = dying,
                };

                EpilogueRules.CountsDeath(facts).ShouldBeFalse();
            }

            [Fact]
            public void Needs_a_session()
            {
                var facts = Crawl with { InSession = false };

                EpilogueRules.CountsDeath(facts).ShouldBeFalse();
            }

            [Fact]
            public void Needs_the_player()
            {
                var facts = Crawl with { HasPlayer = false };

                EpilogueRules.CountsDeath(facts).ShouldBeFalse();
            }
        }

        public class WatchesSoon
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            public void Watches_until_the_pan(
                bool playing,
                bool released,
                bool spectating,
                bool watches)
            {
                EpilogueRules.WatchesSoon(playing, released, spectating)
                    .ShouldBe(watches);
            }
        }

        public class PanTimesOut
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Releases_a_pan_not_released_while_playing(
                bool playing,
                bool released,
                bool times)
            {
                EpilogueRules.PanTimesOut(playing, released).ShouldBe(times);
            }
        }

        public class MayPetDog
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            public void Pets_only_with_the_clip_in_a_session(
                bool inSession,
                bool hasClip,
                bool pets)
            {
                EpilogueRules.MayPetDog(inSession, hasClip).ShouldBe(pets);
            }
        }

        public class SendsOutcomes
        {
            [Theory]
            [InlineData(true, true, 1, true)]
            [InlineData(false, true, 1, false)]
            [InlineData(true, false, 1, false)]
            [InlineData(true, true, 0, false)]
            public void Sends_from_the_host_to_its_peers(
                bool playing,
                bool host,
                int peers,
                bool sends)
            {
                EpilogueRules.SendsOutcomes(playing, host, peers)
                    .ShouldBe(sends);
            }
        }

        public class OutcomesStart
        {
            static readonly OutcomeFacts Joiner = new OutcomeFacts
            {
                Exists = true,
                Playing = true,
                InSession = true,
                Host = HostBurned.Yes,
                Burned = false,
            };

            [Fact]
            public void Takes_the_hosts_outcome_when_it_differs()
            {
                EpilogueRules.OutcomesStart(Joiner)
                    .ShouldBe(OutcomeStep.TakeHost);
            }

            [Fact]
            public void Takes_the_hosts_unburned_forest_too()
            {
                var facts = Joiner with { Host = HostBurned.No, Burned = true };

                EpilogueRules.OutcomesStart(facts)
                    .ShouldBe(OutcomeStep.TakeHost);
            }

            [Fact]
            public void Keeps_its_own_when_the_same()
            {
                var facts = Joiner with { Burned = true };

                EpilogueRules.OutcomesStart(facts).ShouldBe(OutcomeStep.Keep);
            }

            [Fact]
            public void Keeps_its_own_when_nothing_was_heard()
            {
                var facts = Joiner with { Host = HostBurned.Unheard };

                EpilogueRules.OutcomesStart(facts).ShouldBe(OutcomeStep.Keep);
            }

            [Fact]
            public void Sends_the_hosts_own()
            {
                var facts = Joiner with { IsHost = true };

                EpilogueRules.OutcomesStart(facts).ShouldBe(OutcomeStep.Send);
            }

            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Does_nothing_otherwise(
                bool exists,
                bool playing,
                bool session)
            {
                var facts = Joiner with
                {
                    Exists = exists,
                    Playing = playing,
                    InSession = session,
                    IsHost = true,
                };

                EpilogueRules.OutcomesStart(facts).ShouldBe(OutcomeStep.None);
            }
        }

        public class HeardBurned
        {
            [Theory]
            [InlineData("1", HostBurned.Yes)]
            [InlineData("0", HostBurned.No)]
            [InlineData("", HostBurned.No)]
            [InlineData("true", HostBurned.No)]
            public void Reads_only_1_as_burned(string value, HostBurned heard)
            {
                EpilogueRules.HeardBurned(value).ShouldBe(heard);
            }
        }

        public class OffsetsPart
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Moves_a_joiner_in_the_epilogue(
                bool playing,
                bool client,
                bool moves)
            {
                EpilogueRules.OffsetsPart(playing, client).ShouldBe(moves);
            }
        }

        public class Moves
        {
            [Fact]
            public void Moves_exactly_1_away()
            {
                var at = new Vec3(10f, 0f, 10f);

                EpilogueRules.Moves(at, new Vec3(10f, 1f, 10f)).ShouldBeTrue();
            }

            [Fact]
            public void Stays_just_inside_1()
            {
                var at = new Vec3(10f, 0f, 10f);

                EpilogueRules.Moves(at, new Vec3(10.9f, 0f, 10f))
                    .ShouldBeFalse();
            }
        }
    }
}
