namespace DarkwoodMP.Net
{
    /// Where the core writes its log; the plugin passes BepInEx's.
    public interface ILog
    {
        void LogInfo(string text);

        void LogWarning(string text);

        void LogError(string text);
    }
}
