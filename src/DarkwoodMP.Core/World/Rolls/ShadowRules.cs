using System.Collections.Generic;

namespace DarkwoodMP.World.Rolls
{
    /// The shadows' lights (Player.shadowLightsTurnOff), on the host: one
    /// lamp of the generator at the player's big location is chosen to
    /// stay on, every lamp flickers, and then the others go out.
    public static class ShadowRules
    {
        /// Seconds every lamp flickers, and after which the others go out
        /// (scaled time).
        public const float FlickerFor = 8f;

        /// Seconds the other lamps stay out.
        public const float DarkFor = 30f;

        /// Whether the host's shadows turn the lights off: at night, while
        /// the shadows are there, or at any time when the test asks.
        /// shadowsRemoved and day are only looked at when anyTime is false,
        /// so the shell reads them only then.
        public static bool Rolls(bool anyTime, bool shadowsRemoved, bool day)
        {
            if (anyTime)
            {
                return true;
            }

            return !(shadowsRemoved || day);
        }

        /// The indexes of the lamps that can be the one left on, in order:
        /// the generator's power items without the first that is its own
        /// Item, then without any that is gone or kept dark. The host rolls
        /// one of them (UnityEngine.Random.Range over the count); none
        /// means the game's effect doesn't happen.
        public static List<int> Choices(IReadOnlyList<LampFacts> lamps)
        {
            var choices = new List<int>();
            bool ownSkipped = false;
            for (int i = 0; i < lamps.Count; i++)
            {
                var lamp = lamps[i];
                if (lamp.Own && !ownSkipped)
                {
                    ownSkipped = true;
                    continue;
                }

                if (lamp.Missing || lamp.KeptDark)
                {
                    continue;
                }

                choices.Add(i);
            }

            return choices;
        }
    }
}
