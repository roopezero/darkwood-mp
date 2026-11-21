using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public partial class DoorRulesTests
    {
        public class RollBack
        {
            static readonly DoorStatus Shut = new DoorStatus();

            [Fact]
            public void Does_nothing_to_a_door_like_the_hosts()
            {
                var fixes = DoorRules.RollBack(Shut, Shut);

                fixes.ShouldBe(DoorFixes.None);
            }

            [Fact]
            public void Breaks_a_door_the_host_has_broken()
            {
                var host = Shut with { Destroyed = true };

                var fixes = DoorRules.RollBack(host, Shut);

                fixes.ShouldBe(DoorFixes.Break);
            }

            [Fact]
            public void Mends_a_door_the_host_has_whole()
            {
                var ours = Shut with { Destroyed = true };

                var fixes = DoorRules.RollBack(Shut, ours);

                fixes.ShouldBe(DoorFixes.Mend);
            }

            [Fact]
            public void Locks_a_door_the_host_has_locked()
            {
                var host = Shut with { Locked = true };

                var fixes = DoorRules.RollBack(host, Shut);

                fixes.ShouldBe(DoorFixes.Lock);
            }

            [Fact]
            public void Unlocks_a_door_the_host_has_open()
            {
                var ours = Shut with { Locked = true };

                var fixes = DoorRules.RollBack(Shut, ours);

                fixes.ShouldBe(DoorFixes.Unlock);
            }

            [Fact]
            public void Barricades_a_door_the_host_has_barricaded()
            {
                var host = Shut with { Barricaded = true };

                var fixes = DoorRules.RollBack(host, Shut);

                fixes.ShouldBe(DoorFixes.Barricade);
            }

            [Fact]
            public void Removes_a_barricade_the_host_hasnt()
            {
                var ours = Shut with { Barricaded = true };

                var fixes = DoorRules.RollBack(Shut, ours);

                fixes.ShouldBe(DoorFixes.RemoveBarricade);
            }

            [Fact]
            public void Opens_a_door_the_host_has_open()
            {
                var host = Shut with { Opened = true };

                var fixes = DoorRules.RollBack(host, Shut);

                fixes.ShouldBe(DoorFixes.Open);
            }

            [Fact]
            public void Closes_a_door_the_host_has_shut()
            {
                var ours = Shut with { Opened = true };

                var fixes = DoorRules.RollBack(Shut, ours);

                fixes.ShouldBe(DoorFixes.Close);
            }

            [Fact]
            public void Fixes_every_difference_at_once()
            {
                var host = new DoorStatus
                {
                    Opened = true,
                    Barricaded = true,
                    Destroyed = true,
                    Locked = true,
                };

                var fixes = DoorRules.RollBack(host, Shut);

                fixes.ShouldBe(
                    DoorFixes.Break
                    | DoorFixes.Lock
                    | DoorFixes.Barricade
                    | DoorFixes.Open);
            }
        }
    }
}
