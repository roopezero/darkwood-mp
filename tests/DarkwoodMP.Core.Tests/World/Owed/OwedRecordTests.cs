using DarkwoodMP.World.Owed;

namespace DarkwoodMP.Tests.World.Owed
{
    public class OwedRecordTests
    {
        public class Empty
        {
            [Fact]
            public void Holds_nothing_at_first()
            {
                new OwedRecord().Empty.ShouldBeTrue();
            }

            [Fact]
            public void Holds_what_the_host_is_owed()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.Empty.ShouldBeFalse();
            }

            [Fact]
            public void Holds_what_a_joiner_had()
            {
                var record = new OwedRecord();
                record.Give("key", "s1#0");

                record.Empty.ShouldBeFalse();
            }
        }

        public class Repair
        {
            [Fact]
            public void Puts_back_sets_the_file_left_out()
            {
                var record = new OwedRecord { Had = null, HostOwed = null };

                record.Repair();

                record.Empty.ShouldBeTrue();
            }
        }

        public class HostOwe
        {
            [Fact]
            public void Owes_a_new_trigger()
            {
                var record = new OwedRecord();

                record.HostOwe("s1#0").ShouldBeTrue();
            }

            [Fact]
            public void Says_false_for_one_owed_already()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.HostOwe("s1#0").ShouldBeFalse();
            }

            [Fact]
            public void Owes_no_null_id()
            {
                var record = new OwedRecord();

                record.HostOwe(null).ShouldBeFalse();
            }
        }

        public class IsHostOwed
        {
            [Fact]
            public void Finds_an_owed_trigger()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.IsHostOwed("s1#0").ShouldBeTrue();
            }

            [Fact]
            public void Finds_no_other()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.IsHostOwed("s1#1").ShouldBeFalse();
            }

            [Fact]
            public void Finds_no_null_id()
            {
                var record = new OwedRecord();

                record.IsHostOwed(null).ShouldBeFalse();
            }
        }

        public class HostHad
        {
            [Fact]
            public void Clears_an_owed_trigger()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.HostHad("s1#0");

                record.IsHostOwed("s1#0").ShouldBeFalse();
            }

            [Fact]
            public void Says_true_when_it_was_owed()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.HostHad("s1#0").ShouldBeTrue();
            }

            [Fact]
            public void Says_false_when_it_wasnt()
            {
                var record = new OwedRecord();

                record.HostHad("s1#0").ShouldBeFalse();
            }

            [Fact]
            public void Says_false_for_a_null_id()
            {
                var record = new OwedRecord();

                record.HostHad(null).ShouldBeFalse();
            }
        }

        public class Given
        {
            [Fact]
            public void Finds_a_part_given()
            {
                var record = new OwedRecord();
                record.Give("key", "s1#0");

                record.Given("key", "s1#0").ShouldBeTrue();
            }

            [Fact]
            public void Finds_none_for_another_player()
            {
                var record = new OwedRecord();
                record.Give("key", "s1#0");

                record.Given("other", "s1#0").ShouldBeFalse();
            }

            [Fact]
            public void Finds_none_for_another_trigger()
            {
                var record = new OwedRecord();
                record.Give("key", "s1#0");

                record.Given("key", "s1#1").ShouldBeFalse();
            }

            [Fact]
            public void Finds_none_for_a_null_id()
            {
                var record = new OwedRecord();

                record.Given("key", null).ShouldBeFalse();
            }
        }

        public class Give
        {
            [Fact]
            public void Adds_to_the_players_set()
            {
                var record = new OwedRecord();
                record.Give("key", "s1#0");

                record.Give("key", "s1#1");

                record.Had["key"].Count.ShouldBe(2);
            }

            [Fact]
            public void Gives_nothing_for_a_null_id()
            {
                var record = new OwedRecord();

                record.Give("key", null);

                record.Empty.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_hosts_part_alone()
            {
                var record = new OwedRecord();
                record.HostOwe("s1#0");

                record.Give("key", "s1#0");

                record.IsHostOwed("s1#0").ShouldBeTrue();
            }
        }
    }
}
