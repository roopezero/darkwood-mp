using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class RecordRulesTests
    {
        /// Hosting a loaded world with a player and a path.
        static readonly RecordFacts Hosting = new RecordFacts
        {
            IsHost = true,
            InWorld = true,
            HasPlayer = true,
            HasPath = true,
            Start = true,
            Peers = 1,
        };

        public class Keeps
        {
            [Fact]
            public void Keeps_a_record_as_the_host()
            {
                var keeps = RecordRules.Keeps(Hosting);

                keeps.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_record_alone()
            {
                var keeps = RecordRules.Keeps(
                    Hosting with { IsHost = false, Solo = true });

                keeps.ShouldBeTrue();
            }

            public static TheoryData<RecordFacts> Without =>
                new TheoryData<RecordFacts>
                {
                    Hosting with { IsHost = false },
                    Hosting with { InWorld = false },
                    Hosting with { Loading = true },
                    Hosting with { HasPlayer = false },
                    Hosting with { ClientMode = true },
                    Hosting with { HasPath = false },
                };

            [Theory]
            [MemberData(nameof(Without))]
            public void Keeps_none_unless_it_all_holds(RecordFacts record)
            {
                var keeps = RecordRules.Keeps(record);

                keeps.ShouldBeFalse();
            }
        }

        public class Starts
        {
            [Fact]
            public void Starts_one_as_the_host_with_someone_connected()
            {
                var starts = RecordRules.Starts(Hosting);

                starts.ShouldBeTrue();
            }

            [Fact]
            public void Starts_none_as_the_host_alone()
            {
                var starts = RecordRules.Starts(Hosting with { Peers = 0 });

                starts.ShouldBeFalse();
            }

            [Fact]
            public void Starts_one_alone_for_our_own_reset()
            {
                var record = Hosting with
                {
                    Solo = true,
                    Peers = 0,
                    Alone = true,
                };

                var starts = RecordRules.Starts(record);

                starts.ShouldBeTrue();
            }

            [Fact]
            public void Starts_none_alone_for_anything_else()
            {
                var starts = RecordRules.Starts(Hosting with { Solo = true });

                starts.ShouldBeFalse();
            }

            [Fact]
            public void Starts_none_when_the_caller_may_not()
            {
                var starts = RecordRules.Starts(Hosting with { Start = false });

                starts.ShouldBeFalse();
            }

            [Fact]
            public void Starts_none_over_a_record_read()
            {
                var starts = RecordRules.Starts(
                    Hosting with { HasRecord = true });

                starts.ShouldBeFalse();
            }
        }

        public class Writes
        {
            static readonly WriteFacts Saving = new WriteFacts
            {
                HasRecord = true,
                HasPath = true,
                PathCurrent = true,
            };

            [Fact]
            public void Writes_the_record_of_this_world()
            {
                var writes = RecordRules.Writes(Saving);

                writes.ShouldBeTrue();
            }

            public static TheoryData<WriteFacts> Skipped =>
                new TheoryData<WriteFacts>
                {
                    Saving with { Snapshotting = true },
                    Saving with { ClientMode = true },
                    Saving with { HasRecord = false },
                    Saving with { HasPath = false },
                    Saving with { PathCurrent = false },
                };

            [Theory]
            [MemberData(nameof(Skipped))]
            public void Writes_nothing_unless_it_all_holds(WriteFacts write)
            {
                var writes = RecordRules.Writes(write);

                writes.ShouldBeFalse();
            }
        }
    }
}
