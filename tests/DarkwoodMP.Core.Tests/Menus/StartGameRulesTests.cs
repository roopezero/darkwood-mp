using System;
using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class StartGameRulesTests
    {
        static readonly DateTime Monday = new DateTime(2026, 11, 16);

        static readonly SaveFacts InUse = new SaveFacts
        {
            Present = true,
            Active = true,
        };

        static SlotFacts Free(int id) => new SlotFacts { Id = id };

        static SlotFacts Save(int id, int day) => new SlotFacts
        {
            Id = id,
            Used = true,
            Saved = Monday.AddDays(day),
        };

        /// Slot 2 saved on Wednesday, slot 4 on Tuesday, the rest free.
        static readonly SlotFacts[] TwoSaves =
        {
            Free(1), Save(2, 2), Free(3), Save(4, 1), Free(5),
        };

        static readonly SlotFacts[] AllUsed =
        {
            Save(1, 0), Save(2, 2), Save(3, 1), Save(4, 0), Save(5, 0),
        };

        /// Start on slot 2, a compatible save, with the main menu up.
        static readonly StartFacts OnSave = new StartFacts
        {
            MenuReady = true,
            Choice = 2,
            ChosenUsed = true,
            ChosenCompatible = true,
            NewSlot = 1,
        };

        static readonly StartFacts OnNewGame = OnSave with
        {
            Choice = 0,
            ChosenUsed = false,
            ChosenCompatible = false,
        };

        public class SlotCount
        {
            [Theory]
            [InlineData(0, 5)]
            [InlineData(3, 5)]
            [InlineData(5, 5)]
            [InlineData(6, 6)]
            public void Lists_five_slots_or_every_save(int saves, int slots)
            {
                var result = StartGameRules.SlotCount(saves);

                result.ShouldBe(slots);
            }
        }

        public class IsUsed
        {
            [Fact]
            public void Holds_for_an_active_live_profile()
            {
                var used = StartGameRules.IsUsed(InUse);

                used.ShouldBeTrue();
            }

            [Fact]
            public void Frees_a_missing_profile()
            {
                var save = InUse with { Present = false };

                var used = StartGameRules.IsUsed(save);

                used.ShouldBeFalse();
            }

            [Fact]
            public void Frees_an_inactive_profile()
            {
                var used = StartGameRules.IsUsed(InUse with { Active = false });

                used.ShouldBeFalse();
            }

            [Fact]
            public void Frees_a_profile_whose_character_died()
            {
                var used = StartGameRules.IsUsed(InUse with { Dead = true });

                used.ShouldBeFalse();
            }
        }

        public class Problem
        {
            [Fact]
            public void Warns_when_no_profile_is_active_but_files_exist()
            {
                var saves = new[] { InUse with { Active = false } };

                var problem = StartGameRules.Problem(saves, true);

                problem.ShouldBe(
                    "The game's list of saves is missing or damaged, but"
                    + " there are save files. The game's own profiles screen"
                    + " can restore it from a backup.");
            }

            [Fact]
            public void Warns_for_an_empty_list_with_files()
            {
                var problem = StartGameRules.Problem(new SaveFacts[0], true);

                problem.ShouldBe(StartGameRules.Damaged);
            }

            [Fact]
            public void Says_nothing_with_no_files()
            {
                var problem = StartGameRules.Problem(new SaveFacts[0], false);

                problem.ShouldBeNull();
            }

            [Fact]
            public void Says_nothing_with_an_active_profile()
            {
                var saves = new[] { default, InUse };

                var problem = StartGameRules.Problem(saves, true);

                problem.ShouldBeNull();
            }

            [Fact]
            public void Counts_an_active_profile_whose_character_died()
            {
                var saves = new[] { InUse with { Dead = true } };

                var problem = StartGameRules.Problem(saves, true);

                problem.ShouldBeNull();
            }
        }

        public class Choice
        {
            [Fact]
            public void Keeps_New_Game()
            {
                var choice = StartGameRules.Choice(0, TwoSaves);

                choice.ShouldBe(0);
            }

            [Fact]
            public void Keeps_a_slot_that_holds_a_save()
            {
                var choice = StartGameRules.Choice(4, TwoSaves);

                choice.ShouldBe(4);
            }

            [Theory]
            [InlineData(3)]
            [InlineData(9)]
            [InlineData(-1)]
            public void Picks_the_save_written_last_for_anything_else(int was)
            {
                var choice = StartGameRules.Choice(was, TwoSaves);

                choice.ShouldBe(2);
            }

            [Fact]
            public void Picks_New_Game_with_no_save()
            {
                var slots = new[] { Free(1), Free(2) };

                var choice = StartGameRules.Choice(-1, slots);

                choice.ShouldBe(0);
            }

            [Fact]
            public void Picks_the_first_listed_of_saves_written_together()
            {
                var slots = new[] { Free(1), Save(2, 0), Save(3, 0) };

                var choice = StartGameRules.Choice(-1, slots);

                choice.ShouldBe(2);
            }

            [Fact]
            public void Picks_a_save_whose_time_did_not_read_when_alone()
            {
                var slots = new[]
                {
                    Free(1),
                    Save(2, 0) with { Saved = default },
                };

                var choice = StartGameRules.Choice(-1, slots);

                choice.ShouldBe(2);
            }

            [Fact]
            public void Ranks_a_save_whose_time_did_not_read_as_the_oldest()
            {
                var slots = new[]
                {
                    Save(1, 0) with { Saved = default },
                    Save(2, 0),
                };

                var choice = StartGameRules.Choice(-1, slots);

                choice.ShouldBe(2);
            }
        }

        public class NewSlot
        {
            [Fact]
            public void Puts_a_new_game_in_the_first_free_slot()
            {
                var slot = StartGameRules.NewSlot(TwoSaves, 4);

                slot.ShouldBe(1);
            }

            [Fact]
            public void Puts_it_in_the_slot_chosen_when_all_are_used()
            {
                var slot = StartGameRules.NewSlot(AllUsed, 3);

                slot.ShouldBe(3);
            }

            [Fact]
            public void Has_none_when_all_are_used_and_none_was_chosen()
            {
                var slot = StartGameRules.NewSlot(AllUsed, 0);

                slot.ShouldBe(0);
            }
        }

        public class MayStart
        {
            [Fact]
            public void Allows_a_compatible_save()
            {
                var verdict = StartGameRules.MayStart(OnSave);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_new_game_with_a_slot()
            {
                var verdict = StartGameRules.MayStart(OnNewGame);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_before_the_main_menu_is_ready()
            {
                var start = OnSave with { MenuReady = false };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.WaitForMainMenu);
            }

            [Fact]
            public void Refuses_a_slot_with_no_save()
            {
                var start = OnSave with { ChosenUsed = false };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.NoSaveInSlot);
            }

            [Fact]
            public void Refuses_a_save_from_an_older_game()
            {
                var start = OnSave with { ChosenCompatible = false };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.NeedsProfilesScreen);
            }

            [Fact]
            public void Refuses_a_new_game_with_no_slot()
            {
                var start = OnNewGame with { NewSlot = 0 };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.ChooseOverwrite);
            }

            [Fact]
            public void Takes_a_negative_choice_as_New_Game()
            {
                var start = OnNewGame with { Choice = -1, NewSlot = 0 };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.ChooseOverwrite);
            }

            [Fact]
            public void Asks_for_the_main_menu_before_anything_else()
            {
                var start = OnNewGame with { MenuReady = false, NewSlot = 0 };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.WaitForMainMenu);
            }

            [Fact]
            public void Says_no_save_before_an_older_game()
            {
                var start = OnSave with
                {
                    ChosenUsed = false,
                    ChosenCompatible = false,
                };

                var verdict = StartGameRules.MayStart(start);

                verdict.Reason.ShouldBe(Refusal.NoSaveInSlot);
            }

            [Fact]
            public void Ignores_the_new_slot_for_a_save()
            {
                var start = OnSave with { NewSlot = 0 };

                var verdict = StartGameRules.MayStart(start);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class HostsAfterStart
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Hosts_when_ticked_and_in_no_game(
                bool ticked,
                bool inGame,
                bool hosts)
            {
                var result = StartGameRules.HostsAfterStart(ticked, inGame);

                result.ShouldBe(hosts);
            }
        }

        public class CommitsName
        {
            [Theory]
            [InlineData(2, 2, true, true)]
            [InlineData(2, 3, true, false)]
            [InlineData(2, 2, false, false)]
            [InlineData(0, 0, true, false)]
            [InlineData(-1, -1, true, false)]
            public void Writes_the_name_of_the_slot_still_chosen(
                int nameFor,
                int choice,
                bool listed,
                bool commits)
            {
                var result =
                    StartGameRules.CommitsName(nameFor, choice, listed);

                result.ShouldBe(commits);
            }
        }

        public class StoresNewName
        {
            [Theory]
            [InlineData("Swamp run", true)]
            [InlineData("", false)]
            [InlineData("  ", false)]
            [InlineData(null, false)]
            public void Writes_a_name_that_is_not_blank(
                string name,
                bool stores)
            {
                var result = StartGameRules.StoresNewName(name);

                result.ShouldBe(stores);
            }
        }
    }
}
