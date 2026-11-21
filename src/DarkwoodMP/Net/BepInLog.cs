namespace DarkwoodMP.Net
{
    /// The core's log, written to the plugin's BepInEx log (Plugin.Log). It's
    /// read at each write, so one made before Awake still logs.
    public sealed class BepInLog : ILog
    {
        public void LogInfo(string text) => Plugin.Log.LogInfo(text);

        public void LogWarning(string text) => Plugin.Log.LogWarning(text);

        public void LogError(string text) => Plugin.Log.LogError(text);
    }
}
