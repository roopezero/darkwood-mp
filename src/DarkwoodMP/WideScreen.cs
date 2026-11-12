using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// Screens wider than 16:9 (21:9 ultrawide such as 3440x1440 or 2560x1080, 32:9).
    ///
    /// The game zooms its camera by width / 1920 on any screen wider than 1920 pixels, so it always shows 1920
    /// world units across. On 16:9 that's the same as zooming by height, but on 21:9 it zooms in further:
    /// 3440x1440 showed 1920 x 804 units, less of the world than a 1080p screen sees. On screens wider than 16:9
    /// the zoom follows the height instead (1080 units tall, as on 16:9), and the extra width shows more of the
    /// world to the sides. 16:9 and narrower screens are unchanged.
    ///
    /// On its first start the game also caps its resolution at 1920x1200 on bigger screens, which on an
    /// ultrawide monitor is a stretched picture in full screen; there it starts at the monitor's own size.
    /// </summary>
    public static class WideScreen
    {
        private const float Wide = 16f / 9f + 0.01f;

        public static bool IsWide(int width, int height) => height > 0 && (float)width / height > Wide;

        /// <summary>The zoom for this screen, as the game's refreshZoom would, but by height on wide screens.</summary>
        internal static float ZoomFor(int width, int height) => Mathf.Max(1f, height / 1080f);

        internal static void Apply(Controller c)
        {
            if (!c.zoomCamera || !IsWide(Screen.width, Screen.height) || Screen.width <= 1920) return;
            float zoom = ZoomFor(Screen.width, Screen.height);
            if (Mathf.Approximately(zoom, c.cameraZoom)) return;
            c.cameraZoom = zoom;
            var cam = Singleton<CamMain>.Instance;
            if (cam == null) return;
            SetZoom(cam.transform, zoom);
            foreach (var name in new[] { "CamLights", "CamFOV", "CamAmbient", "CamLightning", "CamTopMask" })
                SetZoom(cam.transform.Find(name), zoom);
            c.worldScreenSize = new Vector2(Screen.width * zoom, Screen.height * zoom);
        }

        private static void SetZoom(Transform t, float zoom)
        {
            var tk = t != null ? t.GetComponent<tk2dCamera>() : null;
            if (tk != null) tk.ZoomFactor = zoom;
        }
    }

    /// <summary>The first start on an ultrawide monitor: its own size, full screen, not 1920x1200 stretched.</summary>
    [HarmonyPatch(typeof(Core), "machineSettingsInit")]
    static class WideScreenFirstStartPatch
    {
        static void Postfix()
        {
            var mm = Singleton<MainMenu>.Instance;
            var native = Screen.currentResolution;
            if (mm == null || !mm.firstStart || !WideScreen.IsWide(native.width, native.height)) return;
            if (GameSettings.GetInt("ScreenWidth") == native.width && GameSettings.GetInt("ScreenHeight") == native.height) return;
            GameSettings.SetInt("ScreenWidth", native.width);
            GameSettings.SetInt("ScreenHeight", native.height);
            GameSettings.SetInt("Fullscreen", 1);
            GameSettings.Save();
            Screen.SetResolution(native.width, native.height, true);
            Plugin.Log.LogInfo($"First start on a wide screen: {native.width}x{native.height}, full screen");
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.refreshZoom))]
    static class WideScreenZoomPatch
    {
        static void Postfix(Controller __instance) => WideScreen.Apply(__instance);
    }
}
