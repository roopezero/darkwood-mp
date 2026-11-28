using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class SnapshotRulesTests
    {
        /// A host that can save, whose last snapshot is 10 s old, in this
        /// scene, with its sav.dat.
        static readonly SnapshotFacts Recent = new SnapshotFacts
        {
            Saves = true,
            Now = new Seconds(110f),
            LastAt = new Seconds(100f),
            LastScene = 5,
            Scene = 5,
            HasSave = true,
        };

        static readonly OffThreadFacts Copied = new OffThreadFacts
        {
            HaveStatic = true,
            Setting = true,
        };

        public class Plan
        {
            [Fact]
            public void Reuses_a_recent_snapshot()
            {
                SnapshotRules.Plan(Recent).ShouldBe(SnapshotStep.Reuse);
            }

            [Theory]
            [InlineData(119.9f, SnapshotStep.Reuse)]
            [InlineData(120f, SnapshotStep.Make)]
            public void Reuses_one_for_20_seconds(float now, SnapshotStep step)
            {
                var plan = SnapshotRules.Plan(Recent with
                {
                    Now = new Seconds(now),
                });

                plan.ShouldBe(step);
            }

            [Fact]
            public void Makes_one_for_another_scene()
            {
                var plan = SnapshotRules.Plan(Recent with { Scene = 6 });

                plan.ShouldBe(SnapshotStep.Make);
            }

            [Fact]
            public void Makes_one_whose_save_is_gone()
            {
                var plan = SnapshotRules.Plan(Recent with { HasSave = false });

                plan.ShouldBe(SnapshotStep.Make);
            }

            [Fact]
            public void Makes_the_first_one()
            {
                var plan = SnapshotRules.Plan(Recent with
                {
                    LastAt = new Seconds(-999f),
                    Now = new Seconds(5f),
                    LastScene = 0,
                });

                plan.ShouldBe(SnapshotStep.Make);
            }

            [Fact]
            public void Skips_without_a_save_manager()
            {
                var plan = SnapshotRules.Plan(Recent with { Saves = false });

                plan.ShouldBe(SnapshotStep.Skip);
            }

            [Fact]
            public void Skips_a_game_that_doesnt_save()
            {
                var plan = SnapshotRules.Plan(Recent with { DontSave = true });

                plan.ShouldBe(SnapshotStep.Skip);
            }

            [Fact]
            public void Skips_a_game_loading()
            {
                var plan = SnapshotRules.Plan(Recent with { Loading = true });

                plan.ShouldBe(SnapshotStep.Skip);
            }
        }

        public class OffThread
        {
            [Fact]
            public void Writes_off_the_main_thread()
            {
                SnapshotRules.OffThread(Copied).ShouldBeTrue();
            }

            [Fact]
            public void Saves_the_games_way_without_the_static_part()
            {
                var off = SnapshotRules.OffThread(Copied with
                {
                    HaveStatic = false,
                });

                off.ShouldBeFalse();
            }

            [Fact]
            public void Saves_the_games_way_with_a_new_location()
            {
                var off = SnapshotRules.OffThread(Copied with
                {
                    NewLocation = true,
                });

                off.ShouldBeFalse();
            }

            [Fact]
            public void Saves_the_games_way_when_set()
            {
                var off = SnapshotRules.OffThread(Copied with
                {
                    Setting = false,
                });

                off.ShouldBeFalse();
            }

            [Fact]
            public void Saves_the_games_way_once_broken()
            {
                var off = SnapshotRules.OffThread(Copied with
                {
                    Broken = true,
                });

                off.ShouldBeFalse();
            }
        }

        public class FallsBack
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Falls_back_in_a_world_not_loading(
                bool inWorld,
                bool loading,
                bool falls)
            {
                SnapshotRules.FallsBack(inWorld, loading).ShouldBe(falls);
            }
        }

        public class Reuses
        {
            [Theory]
            [InlineData("a:1:2;", "a:1:2;", true, true)]
            [InlineData("a:1:2;", "a:1:3;", true, false)]
            [InlineData("a:1:2;", "a:1:2;", false, false)]
            [InlineData("a:1:2;", null, true, false)]
            [InlineData("", "", true, false)]
            public void Reuses_the_same_files_in_the_same_scene(
                string stamp,
                string made,
                bool sameScene,
                bool reuses)
            {
                SnapshotRules.Reuses(stamp, made, sameScene).ShouldBe(reuses);
            }
        }

        public class SendsPacked
        {
            [Theory]
            [InlineData(99, 100, true)]
            [InlineData(100, 100, false)]
            public void Packs_only_when_smaller(int packed, int raw, bool does)
            {
                SnapshotRules.SendsPacked(packed, raw).ShouldBe(does);
            }
        }

        public class Source
        {
            [Theory]
            [InlineData(true, WorldSource.Live)]
            [InlineData(false, WorldSource.LastSave)]
            public void Follows_the_setting(bool live, WorldSource source)
            {
                SnapshotRules.Source(live, false).ShouldBe(source);
            }

            [Theory]
            [InlineData(true)]
            [InlineData(false)]
            public void Offers_the_epilogues_save_in_the_epilogue(bool live)
            {
                SnapshotRules.Source(live, true)
                    .ShouldBe(WorldSource.EpilogueSave);
            }
        }
    }
}
