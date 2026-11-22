using System.Collections.Generic;
using DarkwoodMP.Combat.Targeting;

namespace DarkwoodMP.Tests.Combat.Targeting
{
    public class ThreatTableTests
    {
        /// A player's character object, as the shell's key.
        sealed class Who
        {
        }

        static readonly Seconds Start = new Seconds(100f);

        static Seconds At(float later) => new Seconds(100f + later);

        /// A table made at Start whose gone keys are those in the set.
        static ThreatTable<Who> Table(HashSet<Who> gone) =>
            new ThreatTable<Who>(Start, gone.Contains);

        static ThreatTable<Who> Table() => Table(new HashSet<Who>());

        public class Constructor
        {
            [Fact]
            public void Starts_the_decay_clock_at_creation()
            {
                var table = Table();

                table.LastDecay.Value.ShouldBe(100f);
            }

            [Fact]
            public void Starts_the_seen_clock_at_creation()
            {
                var table = Table();

                table.LastSeen.Value.ShouldBe(100f);
            }
        }

        public class Decay
        {
            [Fact]
            public void Halves_a_value_in_fifteen_seconds()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);

                table.Decay(At(15f));

                table.Get(p).ShouldBe(5f, 0.0001f);
            }

            [Fact]
            public void Does_nothing_when_no_time_passed()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);

                table.Decay(Start);

                table.Get(p).ShouldBe(10f);
            }

            [Fact]
            public void Keeps_the_clock_when_it_went_back()
            {
                var table = Table();

                table.Decay(At(-5f));

                table.LastDecay.Value.ShouldBe(100f);
            }

            [Fact]
            public void Moves_the_clock_on()
            {
                var table = Table();

                table.Decay(At(3f));

                table.LastDecay.Value.ShouldBe(103f);
            }

            [Fact]
            public void Keeps_a_value_that_falls_exactly_to_the_least()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 0.1f, Start);

                table.Decay(At(15f));

                table.Values.ContainsKey(p).ShouldBeTrue();
            }

            [Fact]
            public void Forgets_a_value_that_falls_below_the_least()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 0.0999f, Start);

                table.Decay(At(15f));

                table.Values.ContainsKey(p).ShouldBeFalse();
            }

            [Fact]
            public void Forgets_a_gone_key()
            {
                var gone = new HashSet<Who>();
                var table = Table(gone);
                var p = new Who();
                table.Add(p, 10f, Start);
                gone.Add(p);

                table.Decay(At(1f));

                table.Values.Count.ShouldBe(0);
            }

            [Fact]
            public void Keeps_a_gone_key_until_time_passes()
            {
                var gone = new HashSet<Who>();
                var table = Table(gone);
                var p = new Who();
                table.Add(p, 10f, Start);
                gone.Add(p);

                table.Decay(Start);

                table.Values.Count.ShouldBe(1);
            }
        }

        public class Add
        {
            [Fact]
            public void Gives_a_new_player_the_amount()
            {
                var table = Table();
                var p = new Who();

                table.Add(p, 3f, Start);

                table.Get(p).ShouldBe(3f);
            }

            [Fact]
            public void Adds_to_what_a_player_has()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 3f, Start);

                table.Add(p, 4f, Start);

                table.Get(p).ShouldBe(7f);
            }

            [Fact]
            public void Decays_before_adding()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);

                table.Add(p, 1f, At(15f));

                table.Get(p).ShouldBe(6f, 0.0001f);
            }

            [Fact]
            public void Ignores_an_amount_of_zero_without_decaying()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);

                table.Add(p, 0f, At(15f));

                table.LastDecay.Value.ShouldBe(100f);
            }

            [Fact]
            public void Ignores_a_negative_amount()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);

                table.Add(p, -4f, Start);

                table.Get(p).ShouldBe(10f);
            }

            [Fact]
            public void Ignores_a_gone_key_without_decaying()
            {
                var gone = new HashSet<Who>();
                var table = Table(gone);
                var p = new Who();
                gone.Add(p);

                table.Add(p, 5f, At(15f));

                table.LastDecay.Value.ShouldBe(100f);
            }
        }

        public class Get
        {
            [Fact]
            public void Gives_nothing_for_an_unknown_player()
            {
                var table = Table();

                table.Get(new Who()).ShouldBe(0f);
            }

            [Fact]
            public void Gives_nothing_for_a_gone_key_still_stored()
            {
                var gone = new HashSet<Who>();
                var table = Table(gone);
                var p = new Who();
                table.Add(p, 10f, Start);
                gone.Add(p);

                table.Get(p).ShouldBe(0f);
            }

            [Fact]
            public void Gives_the_value_as_of_the_last_decay()
            {
                var table = Table();
                var p = new Who();
                table.Add(p, 10f, Start);
                table.Add(new Who(), 1f, At(15f));

                table.Get(p).ShouldBe(5f, 0.0001f);
            }
        }

        public class TakeSeenSpan
        {
            [Fact]
            public void Gives_the_span_since_the_last_one()
            {
                var table = Table();

                table.TakeSeenSpan(At(0.5f)).ShouldBe(0.5f, 0.0001f);
            }

            [Fact]
            public void Gives_at_most_two_seconds()
            {
                var table = Table();

                table.TakeSeenSpan(At(9f)).ShouldBe(2f);
            }

            [Fact]
            public void Starts_the_next_span()
            {
                var table = Table();

                table.TakeSeenSpan(At(9f));

                table.LastSeen.Value.ShouldBe(109f);
            }
        }
    }
}
