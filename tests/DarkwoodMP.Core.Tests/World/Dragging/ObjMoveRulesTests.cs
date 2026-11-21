using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class ObjMoveRulesTests
    {
        /// A client in the world hears a move from the host.
        static readonly ArrivalFacts FromHost = new ArrivalFacts
        {
            InWorld = true,
            Client = true,
            FromHost = true,
        };

        /// A client applies a slide of a thing it has, nobody dragging it.
        static readonly ApplyFacts Slide = new ApplyFacts
        {
            Client = true,
            Found = true,
        };

        public class Arrive
        {
            [Fact]
            public void Applies_the_hosts_move_on_a_client()
            {
                ObjMoveRules.Arrive(FromHost).ShouldBe(Arrival.Apply);
            }

            [Fact]
            public void Drops_a_clients_move_from_anyone_but_the_host()
            {
                var move = FromHost with { FromHost = false };

                ObjMoveRules.Arrive(move).ShouldBe(Arrival.Drop);
            }

            [Fact]
            public void Drops_a_move_outside_the_world()
            {
                var move = FromHost with { InWorld = false };

                ObjMoveRules.Arrive(move).ShouldBe(Arrival.Drop);
            }

            [Fact]
            public void Checks_a_move_on_the_host()
            {
                var move = new ArrivalFacts { InWorld = true, Host = true };

                ObjMoveRules.Arrive(move).ShouldBe(Arrival.Check);
            }

            [Fact]
            public void Drops_a_move_on_the_host_outside_the_world()
            {
                var move = new ArrivalFacts { Host = true };

                ObjMoveRules.Arrive(move).ShouldBe(Arrival.Drop);
            }
        }

        public class RelayExcept
        {
            [Fact]
            public void Sends_an_end_back_to_its_mover_too()
            {
                ObjMoveRules.RelayExcept(true, 3).ShouldBe(0UL);
            }

            [Fact]
            public void Leaves_out_the_mover_of_a_slide()
            {
                ObjMoveRules.RelayExcept(false, 3).ShouldBe(3UL);
            }
        }

        public class Apply
        {
            [Fact]
            public void Shows_a_slide()
            {
                var step = ObjMoveRules.Apply(Slide);

                step.ShouldBe(new ApplyStep { Action = ApplyAction.Show });
            }

            [Fact]
            public void Checks_a_clients_end_for_our_echo()
            {
                var move = Slide with { Last = true };

                var step = ObjMoveRules.Apply(move);

                step.Action.ShouldBe(ApplyAction.CheckEcho);
            }

            [Fact]
            public void Shows_an_end_on_the_host()
            {
                var move = Slide with { Client = false, Last = true };

                var step = ObjMoveRules.Apply(move);

                step.Action.ShouldBe(ApplyAction.Show);
            }

            [Fact]
            public void Keeps_the_hosts_word_on_what_we_drag()
            {
                var move = Slide with { Ours = true, Last = true };

                var step = ObjMoveRules.Apply(move);

                step.Action.ShouldBe(ApplyAction.KeepHostWord);
            }

            [Fact]
            public void Hears_the_owner_of_a_drag_on_a_client()
            {
                var move = Slide with { Owned = true, Ours = true };

                var step = ObjMoveRules.Apply(move);

                step.Refresh.ShouldBeTrue();
            }

            [Fact]
            public void Doesnt_hear_an_owner_on_the_host()
            {
                var move = Slide with { Client = false, Owned = true };

                var step = ObjMoveRules.Apply(move);

                step.Refresh.ShouldBeFalse();
            }

            [Fact]
            public void Tells_host_ids_of_a_spawned_things_end_not_here()
            {
                var move = Slide with
                {
                    Found = false,
                    Last = true,
                    Spawned = true,
                    Owned = true,
                };

                var step = ObjMoveRules.Apply(move);

                step.ShouldBe(new ApplyStep
                {
                    Action = ApplyAction.TellHostIds,
                });
            }

            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, true, true)]
            public void Only_logs_any_other_move_not_here(
                bool client,
                bool last,
                bool spawned)
            {
                var move = new ApplyFacts
                {
                    Client = client,
                    Last = last,
                    Spawned = spawned,
                };

                var step = ObjMoveRules.Apply(move);

                step.Action.ShouldBe(ApplyAction.NotHere);
            }
        }
    }
}
