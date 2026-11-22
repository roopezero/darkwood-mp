using System;
using System.Collections;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The map test steps behind ITestSteps.
    /// </summary>
    internal sealed class MapSteps : ITestSteps
    {
        public bool DogWatch => MapScenarioDogWatch.On;

        public IEnumerator Run(string step, string[] args, Action<string> reply)
        {
            return MapScenario.Run(step, args, reply);
        }

        public void Stop()
        {
            MapScenario.Stop();
            MapScenarioItems2.AimAt = null;
            MapScenarioItems2.Safe = false;
            MapScenarioEnemies.TraceSpawns = false;
            MapScenarioFarEvents.WatchSteps = false;
            MapScenarioDogWatch.On = false;
        }

        public void ResetCentre()
        {
            MapScenario.ResetCentre();
        }

        public void MarkLogStart()
        {
            MapScenarioIssues.MarkLogStart();
        }
    }
}
