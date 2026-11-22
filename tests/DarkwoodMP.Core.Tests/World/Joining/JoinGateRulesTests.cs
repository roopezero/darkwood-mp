using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class JoinGateRulesTests
    {
        /// A host in its ready world, nothing in the way.
        static readonly ReadyFacts Ready = new ReadyFacts
        {
            InWorld = true,
            WorldReady = true,
        };

        /// A Hello from a joiner the host has a fresh world for, which the
        /// joiner doesn't have yet.
        static readonly AdmitFacts Hello = new AdmitFacts
        {
            Fresh = true,
            InWorld = true,
        };

        /// A live world made at 100 s in scene 5.
        static readonly FreshFacts Made = new FreshFacts
        {
            Made = true,
            MadeScene = 5,
            MadeAt = new Seconds(100f),
            MadeStamp = "sav.dat:10:99;",
            Scene = 5,
            Now = new Seconds(110f),
            LiveWorld = true,
            Saves = true,
            Stamp = "sav.dat:10:99;",
        };

        public class NotReady
        {
            [Fact]
            public void Lets_a_ready_host_take_joiners()
            {
                JoinGateRules.NotReady(Ready).ShouldBe(JoinWait.None);
            }

            [Fact]
            public void Leaves_the_main_menu_to_the_refusal()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    InWorld = false,
                    Film = true,
                });

                why.ShouldBe(JoinWait.None);
            }

            [Fact]
            public void Waits_for_the_test_hold()
            {
                var why = JoinGateRules.NotReady(Ready with { Holding = true });

                why.ShouldBe(JoinWait.Intro);
            }

            [Fact]
            public void Waits_for_the_film()
            {
                var why = JoinGateRules.NotReady(Ready with { Film = true });

                why.ShouldBe(JoinWait.Intro);
            }

            [Fact]
            public void Puts_the_film_over_the_prologue()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    Film = true,
                    Prologue = true,
                    PrologueWaits = true,
                });

                why.ShouldBe(JoinWait.Intro);
            }

            [Fact]
            public void Waits_between_prologue_parts()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    Prologue = true,
                    PrologueWaits = true,
                });

                why.ShouldBe(JoinWait.BetweenPrologueParts);
            }

            [Fact]
            public void Skips_the_later_checks_in_the_prologue()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    Prologue = true,
                    Dreaming = true,
                    Away = true,
                    WorldReady = false,
                });

                why.ShouldBe(JoinWait.None);
            }

            [Fact]
            public void Puts_a_dream_over_being_away()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    Dreaming = true,
                    Away = true,
                });

                why.ShouldBe(JoinWait.Dreaming);
            }

            [Fact]
            public void Puts_being_away_over_loading()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    Away = true,
                    WorldReady = false,
                });

                why.ShouldBe(JoinWait.Away);
            }

            [Fact]
            public void Waits_for_the_world_to_load()
            {
                var why = JoinGateRules.NotReady(Ready with
                {
                    WorldReady = false,
                });

                why.ShouldBe(JoinWait.Loading);
            }
        }

        public class Hold
        {
            static readonly TestHold On = TestHold.Off with { Secs = 30f };

            [Fact]
            public void Does_nothing_while_off()
            {
                var step = JoinGateRules.Hold(TestHold.Off, new Seconds(5f));

                step.Holding.ShouldBeFalse();
            }

            [Fact]
            public void Starts_at_the_first_ask()
            {
                var step = JoinGateRules.Hold(On, new Seconds(5f));

                step.Hold.From.Value.ShouldBe(5f);
            }

            [Theory]
            [InlineData(34.9f, true)]
            [InlineData(35f, false)]
            public void Holds_for_its_seconds(float now, bool holding)
            {
                var started = On with { From = new Seconds(5f) };

                var step = JoinGateRules.Hold(started, new Seconds(now));

                step.Holding.ShouldBe(holding);
            }

            [Fact]
            public void Turns_itself_off_when_over()
            {
                var started = On with { From = new Seconds(5f) };

                var step = JoinGateRules.Hold(started, new Seconds(40f));

                step.Hold.Secs.ShouldBe(0f);
            }

            [Fact]
            public void Ends_a_second_hold_at_once()
            {
                var again = On with { From = new Seconds(5f) };

                var step = JoinGateRules.Hold(again, new Seconds(900f));

                step.Holding.ShouldBeFalse();
            }
        }

        public class Admit
        {
            [Fact]
            public void Offers_the_fresh_world()
            {
                JoinGateRules.Admit(Hello).Step.ShouldBe(AdmitStep.Offer);
            }

            [Fact]
            public void Refuses_while_the_host_plays_the_ending()
            {
                var answer = JoinGateRules.Admit(Hello with
                {
                    Ending = true,
                    Wait = JoinWait.Loading,
                    InWorld = false,
                });

                answer.Refusal.ShouldBe(Refusal.PlayingEnding);
            }

            [Fact]
            public void Waits_with_the_reason()
            {
                var answer = JoinGateRules.Admit(Hello with
                {
                    Wait = JoinWait.Dreaming,
                    Offered = true,
                });

                answer.Wait.ShouldBe(JoinWait.Dreaming);
            }

            [Fact]
            public void Lets_in_with_the_offered_world()
            {
                var answer = JoinGateRules.Admit(Hello with
                {
                    Offered = true,
                    Fresh = false,
                });

                answer.Step.ShouldBe(AdmitStep.LetInOffered);
            }

            [Fact]
            public void Refuses_a_host_out_of_a_world()
            {
                var answer = JoinGateRules.Admit(Hello with
                {
                    Fresh = false,
                    InWorld = false,
                });

                answer.Refusal.ShouldBe(Refusal.NoSavedGame);
            }

            [Fact]
            public void Prepares_a_world_without_a_fresh_one()
            {
                var answer = JoinGateRules.Admit(Hello with { Fresh = false });

                answer.Step.ShouldBe(AdmitStep.Prepare);
            }

            [Fact]
            public void Tells_the_joiner_to_wait_while_preparing()
            {
                var answer = JoinGateRules.Admit(Hello with { Fresh = false });

                answer.Wait.ShouldBe(JoinWait.Preparing);
            }

            [Fact]
            public void Lets_in_a_joiner_with_the_fresh_world()
            {
                var answer = JoinGateRules.Admit(Hello with
                {
                    FreshMatches = true,
                });

                answer.Step.ShouldBe(AdmitStep.LetIn);
            }
        }

        public class Fresh
        {
            [Fact]
            public void Reuses_a_live_world_made_recently()
            {
                JoinGateRules.Fresh(Made).ShouldBeTrue();
            }

            [Fact]
            public void Pins_the_reuse()
            {
                JoinGateRules.SnapshotReuse.Value.ShouldBe(20f);
            }

            [Theory]
            [InlineData(119.9f, true)]
            [InlineData(120f, false)]
            public void Reuses_a_live_world_20_seconds(float now, bool reuse)
            {
                var fresh = JoinGateRules.Fresh(Made with
                {
                    Now = new Seconds(now),
                });

                fresh.ShouldBe(reuse);
            }

            [Fact]
            public void Drops_a_world_of_another_scene()
            {
                JoinGateRules.Fresh(Made with { Scene = 6 }).ShouldBeFalse();
            }

            [Fact]
            public void Has_none_before_one_is_made()
            {
                JoinGateRules.Fresh(Made with { Made = false }).ShouldBeFalse();
            }

            [Fact]
            public void Reuses_a_saved_world_while_its_files_are_the_same()
            {
                var fresh = JoinGateRules.Fresh(Made with
                {
                    LiveWorld = false,
                    Now = new Seconds(9000f),
                });

                fresh.ShouldBeTrue();
            }

            [Fact]
            public void Drops_a_saved_world_whose_files_changed()
            {
                var fresh = JoinGateRules.Fresh(Made with
                {
                    LiveWorld = false,
                    Stamp = "sav.dat:11:99;",
                });

                fresh.ShouldBeFalse();
            }

            [Fact]
            public void Drops_a_saved_world_without_a_profile()
            {
                var fresh = JoinGateRules.Fresh(Made with
                {
                    LiveWorld = false,
                    Saves = false,
                });

                fresh.ShouldBeFalse();
            }
        }

        public class TellsStale
        {
            [Theory]
            [InlineData(false, 0f, true)]
            [InlineData(true, 12f, false)]
            [InlineData(true, 12.1f, true)]
            public void Tells_at_most_every_2_seconds(
                bool told,
                float now,
                bool tells)
            {
                var result = JoinGateRules.TellsStale(
                    told,
                    new Seconds(10f),
                    new Seconds(now));

                result.ShouldBe(tells);
            }
        }

        public class Reloads
        {
            [Theory]
            [InlineData(3, 4, 1, true)]
            [InlineData(0, 4, 1, false)]
            [InlineData(4, 4, 1, false)]
            [InlineData(3, 4, 0, false)]
            public void Reloads_a_new_scene_with_players_in(
                int last,
                int scene,
                int peers,
                bool reloads)
            {
                JoinGateRules.Reloads(last, scene, peers).ShouldBe(reloads);
            }
        }
    }
}
