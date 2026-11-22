using HarmonyLib;

namespace DarkwoodMP.World.Owed
{
    /// <summary>
    /// The game saved: write who is owed their part of fired events beside
    /// it (OwedRecordFile.Saved). It runs with no session too: a host that
    /// stopped hosting still has the record of the session it played.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[]
    {
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    static class SaveManagerSaveOwed
    {
        static void Postfix()
        {
            Plugin.Instance.Host.Owed.Saved();
        }
    }
}
