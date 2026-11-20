using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class ArrivalRulesTests
    {
        /// The host, in a world, with a message from a client.
        static readonly ArrivalFacts OnHost = new ArrivalFacts
        {
            Session = true,
            InWorld = true,
        };

        static readonly ArrivalFacts OnClient = OnHost with
        {
            Client = true,
            FromHost = true,
        };

        public class Takes
        {
            [Fact]
            public void Takes_a_message_from_anyone_on_the_host()
            {
                var taken = ArrivalRules.Takes(OnHost);

                taken.ShouldBeTrue();
            }

            [Fact]
            public void Takes_the_hosts_message_on_a_client()
            {
                var taken = ArrivalRules.Takes(OnClient);

                taken.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_another_clients_message_on_a_client()
            {
                var message = OnClient with { FromHost = false };

                var taken = ArrivalRules.Takes(message);

                taken.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_message_out_of_a_world()
            {
                var message = OnHost with { InWorld = false };

                var taken = ArrivalRules.Takes(message);

                taken.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_message_with_no_session()
            {
                var message = OnHost with { Session = false };

                var taken = ArrivalRules.Takes(message);

                taken.ShouldBeFalse();
            }
        }

        public class TakesFromHost
        {
            [Fact]
            public void Takes_the_hosts_message_on_a_client()
            {
                var taken = ArrivalRules.TakesFromHost(OnClient);

                taken.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_it_on_the_host()
            {
                var taken = ArrivalRules.TakesFromHost(OnHost);

                taken.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_another_clients_message()
            {
                var message = OnClient with { FromHost = false };

                var taken = ArrivalRules.TakesFromHost(message);

                taken.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_message_out_of_a_world()
            {
                var message = OnClient with { InWorld = false };

                var taken = ArrivalRules.TakesFromHost(message);

                taken.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_message_with_no_session()
            {
                var message = OnClient with { Session = false };

                var taken = ArrivalRules.TakesFromHost(message);

                taken.ShouldBeFalse();
            }
        }
    }
}
