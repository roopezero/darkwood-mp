using UnityEngine;

namespace DarkwoodMP.World.Loading
{
    /// LoadingScreen's reading of the game's own loading steps.
    public sealed partial class LoadingScreen
    {
        // ---- the game's own loading steps -----------------------------------

        /// <summary>
        /// What the game's loading is doing now, or null when no world is
        /// loading.
        /// </summary>
        public string GameStage()
        {
            var wg = Singleton<WorldGenerator>.Instance;
            if (wg == null)
            {
                return null;
            }

            if (wg.finished)
            {
                // done: the screen is fading out (or is up for a location,
                // OutsideLocations)
                return null;
            }

            float p = wg.percentLoaded;
            if (wg != _stageWg)
            {
                _stageWg = wg;
                _stageLoading = false;
            }

            if (Core.loadingGame)
            {
                // it goes false again just before Load's last step
                _stageLoading = true;
            }

            if (_stageLoading) // SaveManager.Load
            {
                if (p <= 0f)
                {
                    return "Reading the save...";
                }

                if (p < 50f)
                {
                    return "Placing the saved objects "
                        + $"({Mathf.FloorToInt(p * 2f)}%)...";
                }

                if (p < 70f)
                {
                    return "Fitting the objects together...";
                }

                if (p < 75f)
                {
                    return "Restoring dreams, the map and events...";
                }

                if (p < 80f)
                {
                    return "Restoring the state of the world...";
                }

                if (p < 85f)
                {
                    return "Setting up places and characters...";
                }

                return "Showing the area around you...";
            }

            // WorldGenerator.generateWorld: a new game, or a chapter made from
            // its chapter save
            if (p < 20f)
            {
                return "Laying out the forest...";
            }

            if (p < 30f)
            {
                return "Filling the forest with places...";
            }

            if (p < 40f)
            {
                return "Building the roads...";
            }

            if (p < 70f)
            {
                return "Growing trees and placing objects...";
            }

            if (p < 72f)
            {
                return "Placing you in the world...";
            }

            if (p < 92f)
            {
                return "Working out paths through the forest...";
            }

            return "Bringing in characters and items...";
        }
    }
}
