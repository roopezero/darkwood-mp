using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class PrologueRulesTests
    {
        /// Dreaming the first stage.
        static readonly DreamFacts Stage = new DreamFacts
        {
            Exists = true,
            Dreaming = true,
            HasPreset = true,
            Preset = "dream_tutorial_00",
            TrueName = "dream_tutorial_00",
        };

        /// A joiner in the first stage, loaded 4 s ago, its inputs
        /// forbidden by nothing; the world not ready yet.
        static readonly PrologueFacts Joiner = new PrologueFacts
        {
            InSession = true,
            Dreams = Stage,
            Shared = "dream_tutorial_00",
            Joiner = true,
            HasPlayer = true,
            Generated = true,
            Forbidden = true,
            HeldByNothing = true,
            Now = new Seconds(104.5f),
            Loaded = true,
            LoadedAt = new Seconds(100f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_prologue_limits()
            {
                var limits = new[]
                {
                    PrologueRules.SlotRange,
                    PrologueRules.LetGoAfter,
                };

                limits.ShouldBe(new[] { 5000f, 4f });
            }
        }

        public class IsStage
        {
            [Theory]
            [InlineData("dream_tutorial_00", true)]
            [InlineData("dream_tutorial_01", true)]
            [InlineData("dream_tutorial_02", false)]
            [InlineData(null, false)]
            public void Knows_the_two_stages(string name, bool stage)
            {
                PrologueRules.IsStage(name).ShouldBe(stage);
            }
        }

        public class StageOf
        {
            [Fact]
            public void Names_the_stage_dreamt()
            {
                PrologueRules.StageOf(Stage).ShouldBe("dream_tutorial_00");
            }

            [Fact]
            public void Names_the_stage_by_its_true_name()
            {
                var dreams = Stage with { Preset = "dream_tutorial_00(Clone)" };

                PrologueRules.StageOf(dreams).ShouldBe("dream_tutorial_00");
            }

            [Theory]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            [InlineData(false, false, true)]
            public void Names_a_stage_on_the_way_in_or_out(
                bool prepared,
                bool switching,
                bool wanted)
            {
                var dreams = Stage with
                {
                    Dreaming = false,
                    Prepared = prepared,
                    Switching = switching,
                    Wanted = wanted,
                };

                PrologueRules.StageOf(dreams).ShouldBe("dream_tutorial_00");
            }

            [Fact]
            public void Names_none_out_of_a_dream()
            {
                var dreams = Stage with { Dreaming = false };

                PrologueRules.StageOf(dreams).ShouldBeNull();
            }

            [Fact]
            public void Names_none_in_another_dream()
            {
                var dreams = Stage with { TrueName = "epilog_part1a_dream" };

                PrologueRules.StageOf(dreams).ShouldBeNull();
            }

            [Fact]
            public void Names_none_with_no_preset()
            {
                var dreams = Stage with { HasPreset = false };

                PrologueRules.StageOf(dreams).ShouldBeNull();
            }
        }

        public class InStage
        {
            [Fact]
            public void Counts_a_stage_dreamt()
            {
                PrologueRules.InStage(Stage).ShouldBeTrue();
            }

            [Fact]
            public void Doesnt_count_a_stage_switching()
            {
                var dreams = Stage with { Switching = true };

                PrologueRules.InStage(dreams).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_count_a_stage_only_wanted()
            {
                var dreams = Stage with { Dreaming = false, Wanted = true };

                PrologueRules.InStage(dreams).ShouldBeFalse();
            }
        }

        public class MayJoin
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Lets_a_joiner_in_a_ready_stage(
                bool stage,
                bool ready,
                bool may)
            {
                PrologueRules.MayJoin(stage, ready).ShouldBe(may);
            }
        }

        public class Snapshots
        {
            [Fact]
            public void Keeps_the_stage_in_a_snapshot()
            {
                PrologueRules.Snapshots(true, Stage).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_save_alone()
            {
                PrologueRules.Snapshots(false, Stage).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_stage_on_the_way_in_alone()
            {
                var dreams = Stage with { Dreaming = false, Prepared = true };

                PrologueRules.Snapshots(true, dreams).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_stage_while_switching()
            {
                var dreams = Stage with { Switching = true };

                PrologueRules.Snapshots(true, dreams).ShouldBeTrue();
            }
        }

        public class SlotOf
        {
            static readonly Vec3[] Slots =
            {
                new Vec3(20000f, 0f, 20000f),
                new Vec3(30000f, 0f, 20000f),
            };

            [Fact]
            public void Finds_the_last_slot_exactly_at_the_range()
            {
                var at = new Vec3(35000f, 50f, 15000f);

                var slot = PrologueRules.SlotOf(Slots, at, 2);

                slot.Kind.ShouldBe(SlotKind.Last);
                slot.Index.ShouldBe(1);
            }

            [Fact]
            public void Finds_no_slot_just_past_the_range()
            {
                var at = new Vec3(35000.5f, 0f, 20000f);

                PrologueRules.SlotOf(Slots, at, 2).Kind
                    .ShouldBe(SlotKind.None);
            }

            [Fact]
            public void Finds_another_slot_than_the_last()
            {
                var at = new Vec3(20000f, 0f, 20000f);

                var slot = PrologueRules.SlotOf(Slots, at, 2);

                slot.Kind.ShouldBe(SlotKind.Other);
                slot.Index.ShouldBe(0);
            }

            [Fact]
            public void Takes_the_first_slot_in_range()
            {
                var at = new Vec3(25000f, 0f, 20000f);

                var slot = PrologueRules.SlotOf(Slots, at, 1);

                slot.Kind.ShouldBe(SlotKind.Last);
                slot.Index.ShouldBe(0);
            }
        }

        public class Update
        {
            [Fact]
            public void Lets_the_joiner_go_just_past_the_wait()
            {
                PrologueRules.Update(Joiner).LetGo.ShouldBeTrue();
            }

            [Fact]
            public void Holds_the_joiner_exactly_at_the_wait()
            {
                var facts = Joiner with { Now = new Seconds(104f) };

                PrologueRules.Update(facts).LetGo.ShouldBeFalse();
            }

            [Fact]
            public void Notes_when_the_load_finished()
            {
                var facts = Joiner with { Loaded = false };

                var step = PrologueRules.Update(facts);

                step.Loaded.ShouldBeTrue();
                step.LoadedAt.Value.ShouldBe(104.5f);
                step.LetGo.ShouldBeFalse();
            }

            [Fact]
            public void Holds_a_joiner_held_by_something()
            {
                var facts = Joiner with { HeldByNothing = false };

                PrologueRules.Update(facts).LetGo.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_load_time_while_still_loading()
            {
                var facts = Joiner with { Loading = true };

                var step = PrologueRules.Update(facts);

                step.Loaded.ShouldBeTrue();
                step.LetGo.ShouldBeFalse();
            }

            [Fact]
            public void Forgets_the_load_time_out_of_a_stage()
            {
                var facts = Joiner with { Dreams = default };

                PrologueRules.Update(facts).Loaded.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_the_host_alone()
            {
                var facts = Joiner with { Joiner = false, WorldReady = true };

                var step = PrologueRules.Update(facts);

                step.LetGo.ShouldBeFalse();
                step.MakeFirstPlay.ShouldBeFalse();
            }

            [Fact]
            public void Makes_a_ready_joiner_a_first_play_once()
            {
                var facts = Joiner with { WorldReady = true };

                var step = PrologueRules.Update(facts);

                step.MakeFirstPlay.ShouldBeTrue();
                step.FirstPlaySet.ShouldBeTrue();
                step.LetGo.ShouldBeTrue();
            }

            [Fact]
            public void Neither_lets_go_nor_sets_again_once_set()
            {
                var facts = Joiner with
                {
                    WorldReady = true,
                    FirstPlaySet = true,
                };

                var step = PrologueRules.Update(facts);

                step.MakeFirstPlay.ShouldBeFalse();
                step.LetGo.ShouldBeFalse();
                step.FirstPlaySet.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_it_set_on_the_way_between_stages()
            {
                var facts = Joiner with
                {
                    FirstPlaySet = true,
                    Dreams = Stage with { Switching = true },
                };

                PrologueRules.Update(facts).FirstPlaySet.ShouldBeTrue();
            }

            [Fact]
            public void Clears_it_out_of_the_prologue()
            {
                var facts = Joiner with
                {
                    FirstPlaySet = true,
                    Dreams = default,
                };

                PrologueRules.Update(facts).FirstPlaySet.ShouldBeFalse();
            }

            [Fact]
            public void Clears_only_it_out_of_a_session()
            {
                var facts = Joiner with
                {
                    InSession = false,
                    FirstPlaySet = true,
                };

                var step = PrologueRules.Update(facts);

                step.FirstPlaySet.ShouldBeFalse();
                step.Loaded.ShouldBeTrue();
                step.LetGo.ShouldBeFalse();
                step.Share.ShouldBeNull();
            }

            [Fact]
            public void Shares_a_stage_the_others_dont_dream()
            {
                var facts = Joiner with { Shared = null, Joiner = false };

                PrologueRules.Update(facts).Share
                    .ShouldBe("dream_tutorial_00");
            }

            [Fact]
            public void Shares_nothing_already_shared()
            {
                PrologueRules.Update(Joiner).Share.ShouldBeNull();
            }

            [Fact]
            public void Shares_no_stage_while_switching()
            {
                var facts = Joiner with
                {
                    Shared = null,
                    Dreams = Stage with { Switching = true },
                };

                PrologueRules.Update(facts).Share.ShouldBeNull();
            }
        }

        public class EndsPrologue
        {
            [Theory]
            [InlineData("dream_tutorial_01", true, false, true)]
            [InlineData("dream_tutorial_00", true, false, false)]
            [InlineData("dream_tutorial_01", false, false, false)]
            [InlineData("dream_tutorial_01", true, true, false)]
            public void Ends_it_awake_after_the_last_stage(
                string ended,
                bool dreams,
                bool dreaming,
                bool ends)
            {
                PrologueRules.EndsPrologue(ended, dreams, dreaming)
                    .ShouldBe(ends);
            }
        }
    }
}
