using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class AllowedKillsTests
    {
        static AllowedKills KilledAtTen()
        {
            var kills = new AllowedKills();
            kills.Allow("Wolf", new Seconds(10f));
            return kills;
        }

        public class Recent
        {
            [Fact]
            public void Allows_a_kill_just_now()
            {
                var kills = KilledAtTen();

                kills.Recent("Wolf", new Seconds(10f)).ShouldBeTrue();
            }

            [Fact]
            public void Allows_it_just_inside_the_window()
            {
                var kills = KilledAtTen();

                kills.Recent("Wolf", new Seconds(39.99f)).ShouldBeTrue();
            }

            [Fact]
            public void Stops_exactly_at_the_window()
            {
                var kills = KilledAtTen();

                kills.Recent("Wolf", new Seconds(40f)).ShouldBeFalse();
            }

            [Fact]
            public void Knows_no_other_npc()
            {
                var kills = KilledAtTen();

                kills.Recent("Bear", new Seconds(10f)).ShouldBeFalse();
            }

            [Fact]
            public void Throws_on_a_null_name()
            {
                var kills = KilledAtTen();

                Should.Throw<System.ArgumentNullException>(
                    () => kills.Recent(null, new Seconds(10f)));
            }
        }

        public class Allow
        {
            [Fact]
            public void Starts_the_window_again()
            {
                var kills = KilledAtTen();

                kills.Allow("Wolf", new Seconds(30f));

                kills.Recent("Wolf", new Seconds(50f)).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_an_empty_name()
            {
                var kills = new AllowedKills();

                kills.Allow("", new Seconds(10f));

                kills.Recent("", new Seconds(10f)).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_null_name()
            {
                var kills = new AllowedKills();

                Should.NotThrow(() => kills.Allow(null, new Seconds(10f)));
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_every_kill()
            {
                var kills = KilledAtTen();

                kills.Clear();

                kills.Recent("Wolf", new Seconds(10f)).ShouldBeFalse();
            }
        }
    }
}
