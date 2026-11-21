using System;
using System.Globalization;

namespace DarkwoodMP.Testing
{
    /// How the --dwmp-* launch arguments are read: Plugin.ApplyTestArgs's
    /// Arg, Has and Seconds, the WxH parse of --dwmp-window, which tests
    /// they start, and Plugin.TestMode.
    public static class TestArgRules
    {
        /// Any argument starting with this makes the game a test game.
        public const string TestPrefix = "--dwmp-test";

        public const string Shots = "--dwmp-shots";
        public const string Screen = "--dwmp-screen";
        public const string StartWindow = "--dwmp-test-start-window";
        public const string MenuEntry = "--dwmp-test-menu-entry";
        public const string SoloTest = "--dwmp-solo-test";
        public const string TestWorld = "--dwmp-testworld";

        /// Whether an argument starts with TestPrefix. The shell only ever
        /// turns TestMode on with this, never off: it stays on for the
        /// process, through every later ApplyTestArgs.
        public static bool IsTestGame(string[] args)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith(TestPrefix))
                {
                    return true;
                }
            }

            return false;
        }

        /// The argument after the first one named so, whatever it is (even
        /// another --dwmp- flag), or null when the name is missing or last.
        public static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i >= 0 && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            return null;
        }

        /// Whether the name is one of the arguments.
        public static bool Has(string[] args, string name) =>
            Array.IndexOf(args, name) >= 0;

        /// The whole number after the name (int.TryParse in the game's
        /// culture), as --dwmp-fps, --dwmp-max-players, --dwmp-continue and
        /// --dwmp-test-control read theirs. False without one.
        public static bool TryInt(string[] args, string name, out int value)
        {
            return int.TryParse(Arg(args, name), out value);
        }

        /// The seconds after the name, read in the invariant culture
        /// (--dwmp-test-reload 1.5). False without them.
        public static bool TrySeconds(
            string[] args,
            string name,
            out Seconds value)
        {
            bool ok = float.TryParse(
                Arg(args, name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float secs);
            value = new Seconds(secs);
            return ok;
        }

        /// A "WxH" size (--dwmp-window 1280x720): exactly two whole numbers
        /// split at 'x'. A null text isn't one.
        public static bool TrySize(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (text == null)
            {
                return false;
            }

            var parts = text.Split('x');
            return parts.Length == 2
                && int.TryParse(parts[0], out width)
                && int.TryParse(parts[1], out height);
        }

        /// The window test the arguments start: --dwmp-test-start-window
        /// wins over --dwmp-screen, which needs a value after it.
        public static StartTest WindowTest(string[] args)
        {
            if (Has(args, StartWindow))
            {
                return StartTest.StartWindow;
            }

            if (Arg(args, Screen) != null)
            {
                return StartTest.Screen;
            }

            return StartTest.None;
        }

        /// Whether --dwmp-shots DIR runs the screenshot test on its own:
        /// only with a folder after it and none of the tests that take
        /// screenshots of their own. A --dwmp-solo-test or --dwmp-screen
        /// without a value still counts as given here.
        public static bool ShotsAlone(string[] args)
        {
            return Arg(args, Shots) != null
                && !Has(args, MenuEntry)
                && !Has(args, SoloTest)
                && !Has(args, Screen)
                && !Has(args, TestWorld)
                && !Has(args, StartWindow);
        }
    }
}
