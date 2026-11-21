using System;
using BepInEx;
using DarkwoodMP.World;
using HarmonyLib;

namespace DarkwoodMP
{
    /// <summary>
    /// The map test steps' plugin. BepInEx loads it after the main plugin
    /// (BepInDependency). Without --dwmp-test-control it does nothing: no
    /// patches, no steps. With it, it applies the steps' patches (they watch
    /// and steer the game for the tests) and gives the main plugin the steps
    /// through Plugin.TestSteps.
    /// </summary>
    [BepInPlugin(Guid, Name, PluginInfo.Version)]
    [BepInDependency(PluginInfo.Guid)]
    public sealed class MapTestsPlugin : BaseUnityPlugin
    {
        private const string Guid = "com.darkwoodmp.coop.maptests";
        private const string Name = "DarkwoodMP.MapTests";

        private Harmony _harmony;

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--dwmp-test-control") < 0)
            {
                return;
            }

            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.enabled)
            {
                Logger.LogWarning(
                    "The main plugin isn't running: map test steps off");
                return;
            }

            _harmony = new Harmony(Guid);
            PatchAll();
            plugin.TestSteps = new MapSteps();
        }

        /// <summary>
        /// Patch class by class, as Plugin.PatchAll does, so one that doesn't
        /// fit this build of the game is reported and skipped. These patches
        /// aren't counted in the main plugin's "All N patches applied".
        /// </summary>
        private void PatchAll()
        {
            int ok = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(
                typeof(MapTestsPlugin).Assembly))
            {
                if (type
                    .GetCustomAttributes(typeof(HarmonyPatch), false).Length
                        == 0)
                {
                    continue;
                }

                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    Exception inner = e.InnerException;
                    string message = null;
                    if (inner != null)
                    {
                        message = inner.Message;
                    }

                    if (message == null)
                    {
                        message = e.Message;
                    }

                    Logger.LogError(
                        $"Test patch {type.Name} doesn't fit this build of the "
                            + $"game, skipped: {message}");
                }
            }

            Logger.LogInfo($"{ok} test patches applied");
        }

        private void OnDestroy()
        {
            Harmony harmony = _harmony;
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }
    }
}
