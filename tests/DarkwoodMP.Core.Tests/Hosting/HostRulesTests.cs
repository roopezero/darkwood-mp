using DarkwoodMP.Hosting;

namespace DarkwoodMP.Tests.Hosting
{
    public class HostRulesTests
    {
        /// A player in their own world, before the epilogue.
        static readonly HostFacts InOwnWorld = new HostFacts
        {
            InWorld = true,
        };

        /// The world has loaded, nobody hosts yet, Steam is up but Direct
        /// was asked for.
        static readonly BeginFacts Ready = new BeginFacts
        {
            Host = InOwnWorld,
            SteamUp = true,
        };

        /// Still at the main menu, a second after asking.
        static readonly HostWaitFacts AtMenu = new HostWaitFacts
        {
            Waited = new Seconds(1f),
            MainMenuReady = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_wait_for_the_load()
            {
                var wait = HostRules.LoadStartWait;

                wait.ShouldBe(20f);
            }
        }

        public class MayHost
        {
            [Fact]
            public void Allows_a_player_in_their_own_world()
            {
                var verdict = HostRules.MayHost(InOwnWorld);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_player_at_the_main_menu()
            {
                var host = InOwnWorld with { InWorld = false };

                var verdict = HostRules.MayHost(host);

                verdict.Reason.ShouldBe(Refusal.LoadWorldToHost);
            }

            [Fact]
            public void Refuses_a_copy_of_another_hosts_world()
            {
                var host = InOwnWorld with { HostsCopy = true };

                var verdict = HostRules.MayHost(host);

                verdict.Reason.ShouldBe(Refusal.HostsCopy);
            }

            [Fact]
            public void Refuses_a_world_in_the_epilogue()
            {
                var host = InOwnWorld with { InEpilogue = true };

                var verdict = HostRules.MayHost(host);

                verdict.Reason.ShouldBe(Refusal.EpilogueBegun);
            }

            [Fact]
            public void Puts_no_world_before_a_copy_and_the_epilogue()
            {
                var host = new HostFacts
                {
                    HostsCopy = true,
                    InEpilogue = true,
                };

                var verdict = HostRules.MayHost(host);

                verdict.Reason.ShouldBe(Refusal.LoadWorldToHost);
            }

            [Fact]
            public void Puts_a_copy_before_the_epilogue()
            {
                var host = InOwnWorld with
                {
                    HostsCopy = true,
                    InEpilogue = true,
                };

                var verdict = HostRules.MayHost(host);

                verdict.Reason.ShouldBe(Refusal.HostsCopy);
            }
        }

        public class Wait
        {
            [Fact]
            public void Waits_at_the_menu_for_the_load_to_start()
            {
                var step = HostRules.Wait(AtMenu);

                step.ShouldBe(HostWaitStep.WaitForLoad);
            }

            [Fact]
            public void Waits_at_the_menu_until_just_before_the_limit()
            {
                var wait = AtMenu with { Waited = new Seconds(19.99f) };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.WaitForLoad);
            }

            [Fact]
            public void Gives_up_at_the_menu_once_the_limit_is_reached()
            {
                var wait = AtMenu with { Waited = new Seconds(20f) };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.BackAtMenu);
            }

            [Fact]
            public void Gives_up_when_back_at_the_menu_after_the_load()
            {
                var wait = AtMenu with { LoadStarted = true };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.BackAtMenu);
            }

            [Fact]
            public void Waits_for_the_world_once_the_menu_is_gone()
            {
                var wait = AtMenu with { MainMenuReady = false };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.WaitForWorld);
            }

            [Fact]
            public void Goes_on_once_the_world_is_ready()
            {
                var wait = AtMenu with
                {
                    MainMenuReady = false,
                    WorldReady = true,
                };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.WorldReady);
            }

            [Fact]
            public void Goes_on_at_once_with_the_world_ready_and_the_menu_up()
            {
                var wait = AtMenu with
                {
                    LoadStarted = true,
                    WorldReady = true,
                };

                var step = HostRules.Wait(wait);

                step.ShouldBe(HostWaitStep.WorldReady);
            }
        }

        public class Begin
        {
            [Fact]
            public void Hosts_with_direct_when_asked()
            {
                var begin = HostRules.Begin(Ready);

                begin.Step.ShouldBe(HostBeginStep.Direct);
            }

            [Fact]
            public void Hosts_with_steam_when_asked_and_up()
            {
                var facts = Ready with { SteamAsked = true };

                var begin = HostRules.Begin(facts);

                begin.Step.ShouldBe(HostBeginStep.Steam);
            }

            [Fact]
            public void Hosts_with_direct_when_steam_is_asked_but_down()
            {
                var facts = Ready with
                {
                    SteamAsked = true,
                    SteamUp = false,
                };

                var begin = HostRules.Begin(facts);

                begin.Step.ShouldBe(HostBeginStep.Direct);
            }

            [Fact]
            public void Stops_in_a_running_session()
            {
                var facts = Ready with { InSession = true };

                var begin = HostRules.Begin(facts);

                begin.Step.ShouldBe(HostBeginStep.InSession);
            }

            [Fact]
            public void Stops_with_why_the_player_cant_host()
            {
                var facts = Ready with
                {
                    Host = InOwnWorld with { InEpilogue = true },
                };

                var begin = HostRules.Begin(facts);

                begin.Why.Reason.ShouldBe(Refusal.EpilogueBegun);
            }

            [Fact]
            public void Puts_a_running_session_before_why_the_player_cant()
            {
                var facts = new BeginFacts { InSession = true };

                var begin = HostRules.Begin(facts);

                begin.Step.ShouldBe(HostBeginStep.InSession);
            }
        }

        public class OpensPort
        {
            [Theory]
            [InlineData(false, false, false, false)]
            [InlineData(false, true, false, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, false, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, true, true, true)]
            public void Opens_the_port_only_on_the_fake_router_or_the_setting(
                bool router,
                bool setting,
                bool test,
                bool opens)
            {
                var result = HostRules.OpensPort(router, setting, test);

                result.ShouldBe(opens);
            }
        }

        public class PasswordOf
        {
            [Theory]
            [InlineData(null, "secret", "secret")]
            [InlineData("", "secret", "")]
            [InlineData("now", "secret", "now")]
            public void Takes_a_given_password_over_the_setting(
                string given,
                string setting,
                string used)
            {
                var password = HostRules.PasswordOf(given, setting);

                password.ShouldBe(used);
            }
        }

        public class PlayersOf
        {
            [Theory]
            [InlineData(0, 4, 4)]
            [InlineData(-1, 4, 4)]
            [InlineData(1, 4, 1)]
            [InlineData(9, 4, 9)]
            public void Takes_a_given_limit_above_zero_over_the_setting(
                int given,
                int setting,
                int used)
            {
                var players = HostRules.PlayersOf(given, setting);

                players.ShouldBe(used);
            }
        }
    }
}
