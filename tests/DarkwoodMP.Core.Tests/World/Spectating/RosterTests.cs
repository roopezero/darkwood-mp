using DarkwoodMP.World.Spectating;

namespace DarkwoodMP.Tests.World.Spectating
{
    public class RosterTests
    {
        static readonly Watched Alive = new Watched
        {
            Owner = 5,
            Alive = true,
        };

        public class Playing
        {
            [Fact]
            public void Lists_the_living_sorted_by_owner()
            {
                var remotes = new[]
                {
                    Alive with { Owner = 9 },
                    Alive,
                    Alive with { Owner = 7 },
                };

                var playing = new Roster().Playing(remotes, new Seconds(1f));

                playing.ShouldBe(new ulong[] { 5, 7, 9 });
            }

            [Fact]
            public void Leaves_out_the_dummy()
            {
                var remotes = new[] { Alive with { Dummy = true } };

                new Roster().Playing(remotes, new Seconds(1f)).ShouldBeEmpty();
            }

            [Fact]
            public void Leaves_out_a_spectator()
            {
                var remotes = new[] { Alive with { Spectating = true } };

                new Roster().Playing(remotes, new Seconds(1f)).ShouldBeEmpty();
            }

            [Fact]
            public void Keeps_a_player_dead_just_inside_the_grace()
            {
                var roster = new Roster();
                roster.Playing(new[] { Alive }, new Seconds(100f));
                var dead = new[] { Alive with { Alive = false } };

                roster.Playing(dead, new Seconds(109.9f))
                    .ShouldBe(new ulong[] { 5 });
            }

            [Fact]
            public void Drops_a_player_dead_exactly_the_grace()
            {
                var roster = new Roster();
                roster.Playing(new[] { Alive }, new Seconds(100f));
                var dead = new[] { Alive with { Alive = false } };

                roster.Playing(dead, new Seconds(110f)).ShouldBeEmpty();
            }

            [Fact]
            public void Notes_a_spectator_alive_too()
            {
                var roster = new Roster();
                var spectating = Alive with { Spectating = true };
                roster.Playing(new[] { spectating }, new Seconds(100f));
                var dead = new[] { Alive with { Alive = false } };

                roster.Playing(dead, new Seconds(105f))
                    .ShouldBe(new ulong[] { 5 });
            }

            [Fact]
            public void Drops_a_player_never_seen_alive()
            {
                var dead = new[] { Alive with { Alive = false } };

                new Roster().Playing(dead, new Seconds(0f)).ShouldBeEmpty();
            }
        }

        public class Reset
        {
            [Fact]
            public void Forgets_who_was_alive()
            {
                var roster = new Roster();
                roster.Playing(new[] { Alive }, new Seconds(100f));
                roster.Reset();
                var dead = new[] { Alive with { Alive = false } };

                roster.Playing(dead, new Seconds(101f)).ShouldBeEmpty();
            }
        }
    }
}
