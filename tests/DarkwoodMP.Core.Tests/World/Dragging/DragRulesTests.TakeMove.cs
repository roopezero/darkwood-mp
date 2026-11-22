using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public partial class DragRulesTests
    {
        public class TakeMove
        {
            [Fact]
            public void Takes_a_slide_nobody_drags_in_reach()
            {
                var take = DragRules.TakeMove(Slide);

                take.ShouldBe(new MoveTake { Taken = true });
            }

            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Drops_a_slide_nobody_drags_that_isnt_in_reach(
                bool found,
                bool nearThing,
                bool nearPlace)
            {
                var move = Slide with
                {
                    Found = found,
                    NearThing = nearThing,
                    NearPlace = nearPlace,
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Dropped = true });
            }

            [Fact]
            public void Takes_an_end_nobody_drags_in_reach()
            {
                var move = Slide with { Last = true };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Taken = true });
            }

            [Fact]
            public void Puts_back_an_end_nobody_drags_from_too_far()
            {
                var move = Slide with { Last = true, NearPlace = false };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { PutBack = true });
            }

            [Fact]
            public void Ignores_an_end_of_a_thing_that_isnt_here()
            {
                var move = Slide with { Last = true, Found = false };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(default(MoveTake));
            }

            [Fact]
            public void Takes_and_hears_the_owners_slide()
            {
                var take = DragRules.TakeMove(OwnMove);

                take.ShouldBe(new MoveTake { Refresh = true, Taken = true });
            }

            [Fact]
            public void Ends_the_owners_drag_at_its_end()
            {
                var move = OwnMove with { Last = true };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake
                {
                    Refresh = true,
                    EndDrag = true,
                    Taken = true,
                });
            }

            [Fact]
            public void Takes_a_drag_from_its_owner_out_of_reach()
            {
                var move = OwnMove with { NearThing = false };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { PutBack = true, Disown = true });
            }

            [Fact]
            public void Takes_a_drag_of_a_thing_that_isnt_here_from_its_owner()
            {
                var move = OwnMove with { Found = false };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Disown = true });
            }

            [Fact]
            public void Keeps_an_owner_who_is_stale_moving_it_themselves()
            {
                var move = OwnMove with { Now = new Seconds(60f) };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Refresh = true, Taken = true });
            }

            [Fact]
            public void Corrects_a_mover_who_doesnt_drag_it()
            {
                var take = DragRules.TakeMove(OtherMove);

                take.ShouldBe(new MoveTake { Correct = true });
            }

            [Fact]
            public void Doesnt_correct_again_exactly_half_a_second_later()
            {
                var move = OtherMove with
                {
                    Corrected = true,
                    CorrectedAt = new Seconds(10f),
                    Now = new Seconds(10.5f),
                    Heard = new Seconds(10f),
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(default(MoveTake));
            }

            [Fact]
            public void Corrects_again_past_half_a_second()
            {
                var move = OtherMove with
                {
                    Corrected = true,
                    CorrectedAt = new Seconds(10f),
                    Now = new Seconds(10.51f),
                    Heard = new Seconds(10f),
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Correct = true });
            }

            [Fact]
            public void Doesnt_take_a_move_of_someone_elses_drag_at_the_limit()
            {
                var move = OtherMove with { Now = new Seconds(3f) };

                var take = DragRules.TakeMove(move);

                take.Taken.ShouldBeFalse();
            }

            [Fact]
            public void Frees_a_stale_drag_then_takes_the_slide()
            {
                var move = OtherMove with { Now = new Seconds(3.01f) };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { FreeStale = true, Taken = true });
            }

            [Fact]
            public void Frees_a_stale_drag_then_drops_a_slide_from_too_far()
            {
                var move = OtherMove with
                {
                    Now = new Seconds(3.01f),
                    NearThing = false,
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake
                {
                    FreeStale = true,
                    Dropped = true,
                });
            }

            [Fact]
            public void Frees_a_stale_drag_then_puts_back_an_end_too_far()
            {
                var move = OtherMove with
                {
                    Now = new Seconds(3.01f),
                    Last = true,
                    NearPlace = false,
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake
                {
                    FreeStale = true,
                    PutBack = true,
                });
            }

            [Fact]
            public void Frees_a_stale_drag_then_takes_the_end()
            {
                var move = OtherMove with
                {
                    Now = new Seconds(3.01f),
                    Last = true,
                };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { FreeStale = true, Taken = true });
            }

            [Fact]
            public void Ignores_reach_for_a_move_of_someone_elses_drag()
            {
                var move = OtherMove with { Found = false, NearThing = false };

                var take = DragRules.TakeMove(move);

                take.ShouldBe(new MoveTake { Correct = true });
            }
        }
    }
}
