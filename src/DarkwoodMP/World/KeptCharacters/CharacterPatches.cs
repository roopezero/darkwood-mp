using HarmonyLib;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// The game saved: the host writes the other players' kept characters
    /// beside the save (CharacterRecords.HostSaved). It runs with no
    /// session too, as the records live as long as the game (ModHost.Kept).
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    static class SaveManagerSaveCharacters
    {
        static void Postfix()
        {
            Plugin.Instance.Host.Kept.HostSaved();
        }
    }
}
