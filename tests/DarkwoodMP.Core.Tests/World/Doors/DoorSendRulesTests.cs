using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class DoorSendRulesTests
    {
        static readonly DoorState Shut = new DoorState
        {
            Health = 100,
            BarricadeHealth = 0,
        };

        /// The outermost call, while sharing, that opened the door.
        static readonly DoorSendFacts Opened = new DoorSendFacts
        {
            Outermost = true,
            Sharing = true,
            Before = Shut,
            After = Shut with { Opened = true },
        };

        /// The story clearing a block, outside any patched call.
        static readonly UnblockFacts Cleared = new UnblockFacts
        {
            WasBlocked = true,
            Blocked = false,
            Depth = 0,
            Sharing = true,
        };

        public class SendsChange
        {
            [Fact]
            public void Sends_a_call_that_changed_the_door()
            {
                var sends = DoorSendRules.SendsChange(Opened);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_call_that_changed_nothing()
            {
                var send = Opened with { After = Shut };

                var sends = DoorSendRules.SendsChange(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_call_inside_another()
            {
                var send = Opened with { Outermost = false };

                var sends = DoorSendRules.SendsChange(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_call_while_not_sharing()
            {
                var send = Opened with { Sharing = false };

                var sends = DoorSendRules.SendsChange(send);

                sends.ShouldBeFalse();
            }

            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(2)]
            [InlineData(3)]
            [InlineData(4)]
            public void Sends_a_change_to_any_part(int part)
            {
                var after = Changed(Shut, part);
                var send = Opened with { After = after };

                var sends = DoorSendRules.SendsChange(send);

                sends.ShouldBeTrue();
            }

            static DoorState Changed(DoorState door, int part)
            {
                switch (part)
                {
                    case 0:
                        return door with { Opened = true };
                    case 1:
                        return door with { Barricaded = true };
                    case 2:
                        return door with { Destroyed = true };
                    case 3:
                        return door with { Health = 99 };
                    default:
                        return door with { BarricadeHealth = 1 };
                }
            }
        }

        public class SendsLock
        {
            [Fact]
            public void Sends_the_outermost_lock_while_sharing()
            {
                var sends = DoorSendRules.SendsLock(true, true);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_lock_inside_another_call()
            {
                var sends = DoorSendRules.SendsLock(false, true);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_lock_while_not_sharing()
            {
                var sends = DoorSendRules.SendsLock(true, false);

                sends.ShouldBeFalse();
            }
        }

        public class SendsUnblock
        {
            [Fact]
            public void Sends_a_block_the_story_cleared()
            {
                var sends = DoorSendRules.SendsUnblock(Cleared);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_an_unblock_of_a_door_that_wasnt_blocked()
            {
                var unblock = Cleared with { WasBlocked = false };

                var sends = DoorSendRules.SendsUnblock(unblock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_an_unblock_that_left_it_blocked()
            {
                var unblock = Cleared with { Blocked = true };

                var sends = DoorSendRules.SendsUnblock(unblock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_an_unblock_inside_a_patched_call()
            {
                var unblock = Cleared with { Depth = 1 };

                var sends = DoorSendRules.SendsUnblock(unblock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_an_unblock_while_not_sharing()
            {
                var unblock = Cleared with { Sharing = false };

                var sends = DoorSendRules.SendsUnblock(unblock);

                sends.ShouldBeFalse();
            }
        }
    }
}
