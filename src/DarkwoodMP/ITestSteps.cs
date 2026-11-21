using System;
using System.Collections;

namespace DarkwoodMP
{
    /// <summary>
    /// The map test steps, as TestControl and the plugin's patches see them.
    /// They live in DarkwoodMP.MapTests, a second plugin that is only in the
    /// test game folders; it sets Plugin.TestSteps at its Awake when the game
    /// was started with --dwmp-test-control. Null in a player's game.
    /// </summary>
    internal interface ITestSteps
    {
        /// <summary>
        /// Whether the dog-watch step is logging hits on the stand-ins.
        /// </summary>
        bool DogWatch { get; }

        /// <summary>
        /// "do STEP ARG...": run the step to its end, then call reply once
        /// with "done [INFO]" or "fail REASON".
        /// </summary>
        IEnumerator Run(string step, string[] args, Action<string> reply);

        /// <summary>
        /// Plugin.StopTests: end the running step and turn the steps'
        /// switches back to how a player's game has them.
        /// </summary>
        void Stop();

        /// <summary>
        /// TestControl's reset, before leaving the world: the next test's area
        /// is the test world's again.
        /// </summary>
        void ResetCentre();

        /// <summary>
        /// TestControl's reset, at the main menu: the next test's log scans
        /// start here.
        /// </summary>
        void MarkLogStart();
    }
}
