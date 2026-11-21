using DarkwoodMP.Testing;

namespace DarkwoodMP.Tests.Testing
{
    public class TestArgRulesTests
    {
        public class IsTestGame
        {
            [Theory]
            [InlineData("--dwmp-test-death", true)]
            [InlineData("--dwmp-testworld", true)]
            [InlineData("--dwmp-test", true)]
            [InlineData("--dwmp-host", false)]
            [InlineData("-dwmp-test", false)]
            public void Takes_any_argument_starting_with_the_prefix(
                string arg,
                bool test)
            {
                var args = new[] { "Darkwood.x86_64", arg };

                var result = TestArgRules.IsTestGame(args);

                result.ShouldBe(test);
            }
        }

        public class Arg
        {
            [Fact]
            public void Takes_the_argument_after_the_name()
            {
                var args = new[] { "--dwmp-shots", "dir" };

                var value = TestArgRules.Arg(args, "--dwmp-shots");

                value.ShouldBe("dir");
            }

            [Fact]
            public void Takes_another_flag_as_the_value()
            {
                var args = new[] { "--dwmp-shots", "--dwmp-host" };

                var value = TestArgRules.Arg(args, "--dwmp-shots");

                value.ShouldBe("--dwmp-host");
            }

            [Fact]
            public void Gives_nothing_for_a_name_given_last()
            {
                var args = new[] { "x", "--dwmp-shots" };

                var value = TestArgRules.Arg(args, "--dwmp-shots");

                value.ShouldBeNull();
            }

            [Fact]
            public void Gives_nothing_for_a_missing_name()
            {
                var args = new[] { "--dwmp-host" };

                var value = TestArgRules.Arg(args, "--dwmp-shots");

                value.ShouldBeNull();
            }

            [Fact]
            public void Takes_the_first_of_two()
            {
                var args = new[] { "--dwmp-fps", "30", "--dwmp-fps", "60" };

                var value = TestArgRules.Arg(args, "--dwmp-fps");

                value.ShouldBe("30");
            }
        }

        public class Has
        {
            [Fact]
            public void Finds_a_name_given_last()
            {
                var args = new[] { "x", "--dwmp-host" };

                var found = TestArgRules.Has(args, "--dwmp-host");

                found.ShouldBeTrue();
            }

            [Fact]
            public void Finds_no_name_that_only_starts_the_same()
            {
                var args = new[] { "--dwmp-hosting" };

                var found = TestArgRules.Has(args, "--dwmp-host");

                found.ShouldBeFalse();
            }
        }

        public class TryInt
        {
            [Theory]
            [InlineData("30", true, 30)]
            [InlineData("-2", true, -2)]
            [InlineData("1.5", false, 0)]
            [InlineData("x", false, 0)]
            public void Reads_a_whole_number(string text, bool ok, int read)
            {
                var args = new[] { "--dwmp-fps", text };

                bool result = TestArgRules.TryInt(
                    args,
                    "--dwmp-fps",
                    out int n);

                (result, n).ShouldBe((ok, read));
            }

            [Fact]
            public void Reads_nothing_without_a_value()
            {
                var args = new[] { "--dwmp-fps" };

                bool result = TestArgRules.TryInt(args, "--dwmp-fps", out _);

                result.ShouldBeFalse();
            }
        }

        public class TrySeconds
        {
            [Theory]
            [InlineData("1.5", true, 1.5f)]
            [InlineData("6", true, 6f)]
            [InlineData("1,5", false, 0f)]
            [InlineData("soon", false, 0f)]
            public void Reads_seconds_in_the_invariant_culture(
                string text,
                bool ok,
                float read)
            {
                var args = new[] { "--dwmp-test-menu", text };

                bool result = TestArgRules.TrySeconds(
                    args,
                    "--dwmp-test-menu",
                    out var secs);

                (result, secs.Value).ShouldBe((ok, read));
            }

            [Fact]
            public void Reads_nothing_without_the_name()
            {
                var args = new[] { "--dwmp-host" };

                bool result = TestArgRules.TrySeconds(
                    args,
                    "--dwmp-test-menu",
                    out _);

                result.ShouldBeFalse();
            }
        }

        public class TrySize
        {
            [Fact]
            public void Reads_a_width_and_a_height()
            {
                bool ok = TestArgRules.TrySize(
                    "1280x720",
                    out int w,
                    out int h);

                (ok, w, h).ShouldBe((true, 1280, 720));
            }

            [Theory]
            [InlineData(null)]
            [InlineData("1280")]
            [InlineData("1280x720x2")]
            [InlineData("1280X720")]
            [InlineData("wide x720")]
            public void Refuses_anything_but_two_numbers(string text)
            {
                bool ok = TestArgRules.TrySize(text, out _, out _);

                ok.ShouldBeFalse();
            }
        }

        public class WindowTest
        {
            [Fact]
            public void Puts_the_start_window_before_the_screen_test()
            {
                var args = new[]
                {
                    "--dwmp-screen",
                    "800x600",
                    "--dwmp-test-start-window",
                };

                var test = TestArgRules.WindowTest(args);

                test.ShouldBe(StartTest.StartWindow);
            }

            [Fact]
            public void Runs_the_screen_test_with_a_size()
            {
                var args = new[] { "--dwmp-screen", "800x600" };

                var test = TestArgRules.WindowTest(args);

                test.ShouldBe(StartTest.Screen);
            }

            [Fact]
            public void Runs_no_screen_test_without_a_size()
            {
                var args = new[] { "--dwmp-screen" };

                var test = TestArgRules.WindowTest(args);

                test.ShouldBe(StartTest.None);
            }
        }

        public class ShotsAlone
        {
            [Fact]
            public void Runs_the_shots_with_a_folder()
            {
                var args = new[] { "--dwmp-shots", "dir" };

                var alone = TestArgRules.ShotsAlone(args);

                alone.ShouldBeTrue();
            }

            [Fact]
            public void Runs_no_shots_without_a_folder()
            {
                var args = new[] { "--dwmp-shots" };

                var alone = TestArgRules.ShotsAlone(args);

                alone.ShouldBeFalse();
            }

            [Theory]
            [InlineData("--dwmp-test-menu-entry")]
            [InlineData("--dwmp-solo-test")]
            [InlineData("--dwmp-screen")]
            [InlineData("--dwmp-testworld")]
            [InlineData("--dwmp-test-start-window")]
            public void Leaves_the_shots_to_a_test_that_takes_its_own(
                string other)
            {
                var args = new[] { other, "--dwmp-shots", "dir" };

                var alone = TestArgRules.ShotsAlone(args);

                alone.ShouldBeFalse();
            }
        }
    }
}
