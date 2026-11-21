using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class LeftPlayersTests
    {
        const ulong Id = 7;

        static LeftPlayers LeftAtTen()
        {
            var left = new LeftPlayers();
            left.Left(Id, new Seconds(10f));
            return left;
        }

        public class JustNow
        {
            [Fact]
            public void Knows_nothing_of_a_player_who_never_left()
            {
                var left = new LeftPlayers();

                var just = left.JustNow(Id, new Seconds(10f));

                just.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_player_just_under_5_seconds_after()
            {
                var left = LeftAtTen();

                var just = left.JustNow(Id, new Seconds(14.99f));

                just.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_player_back_at_5_seconds()
            {
                var left = LeftAtTen();

                var just = left.JustNow(Id, new Seconds(15f));

                just.ShouldBeFalse();
            }

            [Fact]
            public void Forgets_the_player_at_the_first_ask_after()
            {
                var left = LeftAtTen();
                left.JustNow(Id, new Seconds(15f));

                var just = left.JustNow(Id, new Seconds(11f));

                just.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_player_while_still_ignored()
            {
                var left = LeftAtTen();
                left.JustNow(Id, new Seconds(11f));

                var just = left.JustNow(Id, new Seconds(12f));

                just.ShouldBeTrue();
            }
        }

        public class Joined
        {
            [Fact]
            public void Takes_a_player_back_at_once()
            {
                var left = LeftAtTen();
                left.Joined(Id);

                var just = left.JustNow(Id, new Seconds(11f));

                just.ShouldBeFalse();
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_everyone()
            {
                var left = LeftAtTen();
                left.Clear();

                var just = left.JustNow(Id, new Seconds(11f));

                just.ShouldBeFalse();
            }
        }
    }
}
