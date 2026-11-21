using DarkwoodMP.World.Skills;
using static DarkwoodMP.Tests.World.Skills.Tiers;

namespace DarkwoodMP.Tests.World.Skills
{
    public class SkillPanelRulesTests
    {
        /// In a world, at ease, with the tiers, and the panel closed.
        static readonly PanelFacts AtEase = new PanelFacts
        {
            HasPlayer = true,
            InWorld = true,
            Health = 100f,
            HasTiers = true,
        };

        public class CanOpen
        {
            [Fact]
            public void Opens_in_a_world_at_ease()
            {
                var note = SkillPanelRules.CanOpen(AtEase);

                note.ShouldBe(SkillNote.None);
            }

            [Fact]
            public void Opens_with_any_health_above_zero()
            {
                var note = SkillPanelRules.CanOpen(
                    AtEase with { Health = 0.01f });

                note.ShouldBe(SkillNote.None);
            }

            public static TheoryData<PanelFacts, SkillNote> Refused =>
                new TheoryData<PanelFacts, SkillNote>
                {
                    { AtEase with { HasPlayer = false }, SkillNote.NotInWorld },
                    { AtEase with { InWorld = false }, SkillNote.NotInWorld },
                    { AtEase with { Loading = true }, SkillNote.NotInWorld },
                    { AtEase with { MainMenu = true }, SkillNote.NotInWorld },
                    {
                        AtEase with { LoadingGame = true },
                        SkillNote.NotInWorld
                    },
                    { AtEase with { Paused = true }, SkillNote.Busy },
                    { AtEase with { ForbidInputs = true }, SkillNote.Busy },
                    { AtEase with { InMenu = true }, SkillNote.Busy },
                    { AtEase with { Dying = true }, SkillNote.Busy },
                    { AtEase with { Health = 0f }, SkillNote.Busy },
                    { AtEase with { Watching = true }, SkillNote.Watching },
                    { AtEase with { HasTiers = false }, SkillNote.NoSkills },
                    {
                        AtEase with { Loading = true, Paused = true },
                        SkillNote.NotInWorld
                    },
                    {
                        AtEase with { Dying = true, Watching = true },
                        SkillNote.Busy
                    },
                    {
                        AtEase with { Watching = true, HasTiers = false },
                        SkillNote.Watching
                    },
                };

            [Theory]
            [MemberData(nameof(Refused))]
            public void Says_why_it_cant_open(PanelFacts panel, SkillNote why)
            {
                var note = SkillPanelRules.CanOpen(panel);

                note.ShouldBe(why);
            }
        }

        public class ClosesNow
        {
            static readonly PanelFacts Open = AtEase with { Open = true };

            [Fact]
            public void Stays_open_in_a_world_at_ease()
            {
                var closes = SkillPanelRules.ClosesNow(Open);

                closes.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_while_closed()
            {
                var closes = SkillPanelRules.ClosesNow(
                    AtEase with { InWorld = false });

                closes.ShouldBeFalse();
            }

            public static TheoryData<PanelFacts> Closing =>
                new TheoryData<PanelFacts>
                {
                    Open with { InWorld = false },
                    Open with { MainMenu = true },
                    Open with { HasPlayer = false },
                    Open with { InMenu = true },
                    Open with { Paused = true },
                    Open with { Dying = true },
                };

            [Theory]
            [MemberData(nameof(Closing))]
            public void Closes_when_a_menu_opens_or_the_world_goes(
                PanelFacts panel)
            {
                var closes = SkillPanelRules.ClosesNow(panel);

                closes.ShouldBeTrue();
            }

            public static TheoryData<PanelFacts> Staying =>
                new TheoryData<PanelFacts>
                {
                    Open with { Health = 0f },
                    Open with { Loading = true },
                    Open with { ForbidInputs = true },
                    Open with { Watching = true },
                    Open with { LoadingGame = true },
                    Open with { HasTiers = false },
                };

            [Theory]
            [MemberData(nameof(Staying))]
            public void Stays_open_for_what_only_stops_it_opening(
                PanelFacts panel)
            {
                var closes = SkillPanelRules.ClosesNow(panel);

                closes.ShouldBeFalse();
            }
        }

        public class ReadsKey
        {
            [Theory]
            [InlineData(false, false, false, true)]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            [InlineData(false, false, true, false)]
            public void Reads_the_key_unless_something_else_has_it(
                bool coop,
                bool keyboard,
                bool off,
                bool reads)
            {
                var key = new KeyFacts
                {
                    CoopMenuOpen = coop,
                    KeyboardTaken = keyboard,
                    Off = off,
                };

                var read = SkillPanelRules.ReadsKey(key);

                read.ShouldBe(reads);
            }
        }

        public class TakesClick
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Takes_a_click_on_a_skill_that_can_be_picked(
                bool allowed,
                bool learned,
                bool takes)
            {
                var click = SkillPanelRules.TakesClick(allowed, learned);

                click.ShouldBe(takes);
            }
        }

        public class Pick
        {
            static readonly PickFacts Click = new PickFacts
            {
                Tiers = Three,
                Chosen = Set(),
                Clicked = "a1",
            };

            [Fact]
            public void Picks_a_good_skill()
            {
                var picks = SkillPanelRules.Pick(Click);

                picks.ShouldBe(new SkillPicks { Good = "a1" });
            }

            [Fact]
            public void Unpicks_the_good_skill_clicked_again()
            {
                var picks = SkillPanelRules.Pick(
                    Click with { Good = "a1", Negative = "n1" });

                picks.ShouldBe(new SkillPicks { Negative = "n1" });
            }

            [Fact]
            public void Picks_a_negative_skill_beside_the_good_one()
            {
                var click = Click with
                {
                    Clicked = "n2",
                    ClickedNegative = true,
                    Good = "a1",
                    Negative = "n1",
                };

                var picks = SkillPanelRules.Pick(click);

                picks.ShouldBe(new SkillPicks { Good = "a1", Negative = "n2" });
            }

            [Fact]
            public void Drops_the_negative_skill_of_another_tier()
            {
                var picks = SkillPanelRules.Pick(
                    Click with { Clicked = "b1", Negative = "n1" });

                picks.ShouldBe(new SkillPicks { Good = "b1" });
            }

            [Fact]
            public void Drops_the_good_skill_of_another_tier()
            {
                var click = Click with
                {
                    Clicked = "m1",
                    ClickedNegative = true,
                    Good = "a1",
                };

                var picks = SkillPanelRules.Pick(click);

                picks.ShouldBe(new SkillPicks { Negative = "m1" });
            }

            [Fact]
            public void Drops_a_negative_skill_the_good_one_doesnt_need()
            {
                var click = Click with
                {
                    Chosen = Set("a2"),
                    Negative = "n1",
                };

                var picks = SkillPanelRules.Pick(click);

                picks.ShouldBe(new SkillPicks { Good = "a1" });
            }

            [Fact]
            public void Keeps_both_without_a_player()
            {
                var click = Click with { Chosen = null, Negative = "n1" };

                var picks = SkillPanelRules.Pick(click);

                picks.ShouldBe(new SkillPicks { Good = "a1", Negative = "n1" });
            }

            [Fact]
            public void Counts_two_unknown_names_as_one_tier()
            {
                var click = Click with
                {
                    Clicked = "x",
                    Negative = "y",
                    Chosen = null,
                };

                var picks = SkillPanelRules.Pick(click);

                picks.ShouldBe(new SkillPicks { Good = "x", Negative = "y" });
            }
        }

        public class GoodAllowed
        {
            [Fact]
            public void Allows_a_skill_MayTake_allows()
            {
                var allowed = SkillPanelRules.GoodAllowed(First);

                allowed.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_skill_MayTake_refuses()
            {
                var allowed = SkillPanelRules.GoodAllowed(
                    First with { Good = "b1" });

                allowed.ShouldBeFalse();
            }
        }

        public class NegativeAllowed
        {
            [Fact]
            public void Allows_the_negative_skill_the_good_one_needs()
            {
                var allowed = SkillPanelRules.NegativeAllowed(First);

                allowed.ShouldBeTrue();
            }

            [Fact]
            public void Allows_an_owed_one_with_no_good_skill_picked()
            {
                var allowed = SkillPanelRules.NegativeAllowed(
                    First with { Good = null });

                allowed.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_any_when_the_good_one_needs_none()
            {
                var choice = First with
                {
                    Level = 3,
                    Chosen = Set("a2"),
                    Good = "c1",
                    Negative = "m1",
                };

                var allowed = SkillPanelRules.NegativeAllowed(choice);

                allowed.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_one_MayTakeNegative_refuses()
            {
                var allowed = SkillPanelRules.NegativeAllowed(
                    First with { Good = null, Negative = "m1" });

                allowed.ShouldBeFalse();
            }
        }

        public class NegativeSent
        {
            [Fact]
            public void Sends_the_negative_skill_when_it_is_needed()
            {
                var sent = SkillPanelRules.NegativeSent(First);

                sent.ShouldBe("n1");
            }

            [Fact]
            public void Sends_none_when_it_isnt_needed()
            {
                var sent = SkillPanelRules.NegativeSent(
                    First with { Chosen = Set("a2") });

                sent.ShouldBeNull();
            }

            [Fact]
            public void Sends_none_without_a_good_skill()
            {
                var sent = SkillPanelRules.NegativeSent(
                    First with { Good = null });

                sent.ShouldBeNull();
            }
        }

        public class Ready
        {
            [Fact]
            public void Turns_on_for_a_choice_Check_allows()
            {
                var ready = SkillPanelRules.Ready(First, false);

                ready.ShouldBeTrue();
            }

            [Fact]
            public void Stays_off_while_waiting_for_the_host()
            {
                var ready = SkillPanelRules.Ready(First, true);

                ready.ShouldBeFalse();
            }

            [Fact]
            public void Stays_off_with_no_good_skill_picked()
            {
                var ready = SkillPanelRules.Ready(
                    First with { Good = null },
                    false);

                ready.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_negative_skill_that_isnt_needed()
            {
                var choice = First with { Level = 2, Chosen = Set("a2") };

                var ready = SkillPanelRules.Ready(choice, false);

                ready.ShouldBeTrue();
            }

            [Fact]
            public void Stays_off_while_a_needed_negative_skill_is_missing()
            {
                var ready = SkillPanelRules.Ready(
                    First with { Negative = null },
                    false);

                ready.ShouldBeFalse();
            }
        }

        public class Footer
        {
            static readonly FooterFacts Picked = new FooterFacts
            {
                Good = "a1",
                Negative = "n1",
                Points = 1,
                NeedsNegative = true,
            };

            public static TheoryData<FooterFacts, PanelNote> Lines =>
                new TheoryData<FooterFacts, PanelNote>
                {
                    { Picked, PanelNote.None },
                    {
                        Picked with { HasMessage = true, Waiting = true },
                        PanelNote.Message
                    },
                    {
                        Picked with { Waiting = true, Good = null },
                        PanelNote.Asking
                    },
                    { Picked with { Good = null }, PanelNote.None },
                    {
                        Picked with { Good = null, Points = 0 },
                        PanelNote.NoPoints
                    },
                    {
                        Picked with { Negative = null },
                        PanelNote.ChooseNegative
                    },
                    {
                        Picked with { Negative = null, NeedsNegative = false },
                        PanelNote.None
                    },
                    {
                        Picked with { Points = 0 },
                        PanelNote.None
                    },
                };

            [Theory]
            [MemberData(nameof(Lines))]
            public void Says_the_first_thing_that_applies(
                FooterFacts footer,
                PanelNote line)
            {
                var note = SkillPanelRules.Footer(footer);

                note.ShouldBe(line);
            }
        }

        public class Detail
        {
            [Fact]
            public void Says_a_skill_had_is_learned()
            {
                var detail = SkillPanelRules.Detail(
                    First with { Chosen = Set("b1") },
                    "b1");

                detail.Note.ShouldBe(PanelNote.Learned);
            }

            [Fact]
            public void Says_an_owed_negative_skill_comes_with_the_first()
            {
                var detail = SkillPanelRules.Detail(First, "n2");

                detail.Note.ShouldBe(PanelNote.TakenWithFirst);
            }

            [Fact]
            public void Gives_why_a_negative_skill_cant_be_taken()
            {
                var detail = SkillPanelRules.Detail(First, "m1");

                detail.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.TierClosed, "3"));
            }

            [Fact]
            public void Gives_why_a_good_skill_cant_be_taken()
            {
                var detail = SkillPanelRules.Detail(First, "c1");

                detail.Note.ShouldBe(PanelNote.Refused);
            }

            [Fact]
            public void Says_a_first_skill_comes_with_a_negative_one()
            {
                var detail = SkillPanelRules.Detail(First, "a2");

                detail.Note.ShouldBe(PanelNote.ComesWithNegative);
            }

            [Fact]
            public void Says_a_skill_can_be_learned()
            {
                var detail = SkillPanelRules.Detail(
                    First with { Level = 2, Chosen = Set("a1", "n1") },
                    "a2");

                detail.Note.ShouldBe(PanelNote.CanBeLearned);
            }

            [Fact]
            public void Leaves_the_verdict_allowed_for_a_note()
            {
                var detail = SkillPanelRules.Detail(First, "a2");

                detail.Verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Looks_at_the_shown_skill_not_the_picks()
            {
                var detail = SkillPanelRules.Detail(
                    First with { Good = "c1", Negative = "m1" },
                    "n1");

                detail.Note.ShouldBe(PanelNote.TakenWithFirst);
            }
        }

        public class WatchesAction
        {
            [Theory]
            [InlineData("Primary Action")]
            [InlineData("Aim")]
            [InlineData("Secondary Attack")]
            [InlineData("Attack")]
            public void Watches_the_games_mouse_actions(string action)
            {
                var watches = SkillPanelRules.WatchesAction(true, action);

                watches.ShouldBeTrue();
            }

            [Theory]
            [InlineData("Run")]
            [InlineData("attack")]
            [InlineData(null)]
            public void Leaves_other_actions_alone(string action)
            {
                var watches = SkillPanelRules.WatchesAction(true, action);

                watches.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_every_action_alone_while_closed()
            {
                var watches = SkillPanelRules.WatchesAction(false, "Aim");

                watches.ShouldBeFalse();
            }
        }

        public class TakesEscape
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Takes_escape_while_open_with_no_film_to_skip(
                bool open,
                bool forbid,
                bool takes)
            {
                var escape = SkillPanelRules.TakesEscape(open, forbid);

                escape.ShouldBe(takes);
            }
        }
    }
}
