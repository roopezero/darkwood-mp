using UnityEngine;

namespace DarkwoodMP.World.Spectating
{
    /// Spectator's view: the player's light cone, opened all around while
    /// watching and put back after.
    internal sealed partial class Spectator
    {
        /// <summary>
        /// What the player sees is drawn through two lights on the player
        /// (Player.FOVLight, the visible cone, and FOVLogic, which shows and
        /// hides things in it) whose LightConeAngle Player.getFOVAngle sets
        /// every frame from currentFOV. A dead player's Update stops before
        /// that, so the cone stays at its last angle and facing, and a
        /// spectator saw only that slice around the player they watch. While
        /// spectating we open both to 360 degrees, as the game does in its
        /// cutscenes (currentFOV 370), and set currentFOV to match
        /// (Player.isInSight uses it). Called every frame while spectating;
        /// Restore puts the saved values back.
        /// </summary>
        internal void AllAround(Player p)
        {
            if (p == null || p.FOVLogic == null)
            {
                return;
            }

            var light = FovLight(p);
            if (float.IsNaN(_coneLight))
            {
                if (light != null)
                {
                    _coneLight = light.LightConeAngle;
                }
                else
                {
                    _coneLight = p.FOVLogic.LightConeAngle;
                }

                _coneLogic = p.FOVLogic.LightConeAngle;
                _currentFov = CurrentFov(p);
            }

            if (light != null && light.LightConeAngle < 360f)
            {
                light.LightConeAngle = 360f;
            }

            if (p.FOVLogic.LightConeAngle < 360f)
            {
                p.FOVLogic.LightConeAngle = 360f;
            }

            CurrentFov(p) = 370f;
        }

        private void RestoreCone()
        {
            if (float.IsNaN(_coneLight))
            {
                return;
            }

            var p = Player.Instance;
            if (p != null && p.FOVLogic != null)
            {
                var light = FovLight(p);
                if (light != null)
                {
                    light.LightConeAngle = _coneLight;
                }

                p.FOVLogic.LightConeAngle = _coneLogic;
                CurrentFov(p) = _currentFov;
            }

            _coneLight = float.NaN;
        }

        /// <summary>
        /// For the tests: the cone saved when spectating began (NaN light:
        /// none).
        /// </summary>
        internal (float light, float logic, float current) SavedCone =>
            (_coneLight, _coneLogic, _currentFov);

        /// <summary>
        /// For the tests: stop spectating as a world reload does.
        /// </summary>
        internal void TestEnd() => End();

        /// <summary>
        /// For the tests: the visible cone's and the logic cone's angles, and
        /// currentFOV.
        /// </summary>
        internal (float light, float logic, float current) ConeAngles()
        {
            var p = Player.Instance;
            if (p == null || p.FOVLogic == null)
            {
                return (-1f, -1f, -1f);
            }

            var light = FovLight(p);
            float lightConeAngle = -1f;
            if (light != null)
            {
                lightConeAngle = light.LightConeAngle;
            }

            return (lightConeAngle, p.FOVLogic.LightConeAngle, CurrentFov(p));
        }
    }
}
