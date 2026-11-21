using System.Collections.Generic;

namespace DarkwoodMP.World.Scripted
{
    /// Host: the trips in events a client is about to run, which move
    /// their player by themselves (the plugin's ScriptedEvents.ExpectTrips).
    public static class TripRules
    {
        /// How long past the step's start a trip may still land.
        public const float Margin = 10f;

        /// The shortest fade to black before a trip: the game's default.
        public const float MinFade = 0.5f;

        /// Each trip with a target, and how long to expect it: the margin,
        /// the delays of the steps up to it (none below zero, disabled
        /// ones left out) and its fade.
        public static List<TripWait> Waits(IReadOnlyList<TripStep> steps)
        {
            var waits = new List<TripWait>();
            float delay = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (!step.Exists || step.Disabled)
                {
                    continue;
                }

                delay += Max(0f, step.Delay.Value);
                if (!step.Trip || !step.HasTarget)
                {
                    continue;
                }

                float fade = Max(step.Fade.Value, MinFade);
                var wait = new Seconds(Margin + delay + fade);
                waits.Add(new TripWait(i, wait));
            }

            return waits;
        }

        /// As Mathf.Max: a when it is larger, else b.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
