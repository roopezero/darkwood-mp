using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public partial class ObjRefRulesTests
    {
        public class NeedsPlace
        {
            static readonly NamingFacts Unnamed = new NamingFacts
            {
                Exists = true,
                SendLimit = 1000,
            };

            [Fact]
            public void Needs_the_place_of_an_object_nothing_else_names()
            {
                ObjRefRules.NeedsPlace(Unnamed).ShouldBeTrue();
            }

            [Fact]
            public void Needs_it_for_a_save_id_at_the_send_limit()
            {
                var obj = Unnamed with { SaveId = 1000 };

                ObjRefRules.NeedsPlace(obj).ShouldBeTrue();
            }

            [Fact]
            public void Does_not_need_it_for_a_gone_object()
            {
                var obj = Unnamed with { Exists = false };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }

            [Fact]
            public void Does_not_need_it_for_a_spawned_object()
            {
                var obj = Unnamed with { Spawned = true };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }

            [Fact]
            public void Does_not_need_it_for_an_enemy()
            {
                var obj = Unnamed with { Enemy = true };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }

            [Fact]
            public void Does_not_need_it_for_a_shared_save_id()
            {
                var obj = Unnamed with { SaveId = 999 };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }

            [Fact]
            public void Does_not_need_it_for_an_id_the_host_gave_out()
            {
                var obj = Unnamed with { Shared = true };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }

            [Fact]
            public void Does_not_need_it_for_a_pinned_object()
            {
                var obj = Unnamed with { Pinned = true, PinnedKey = 4 };

                ObjRefRules.NeedsPlace(obj).ShouldBeFalse();
            }
        }
    }
}
