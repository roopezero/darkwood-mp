using HarmonyLib;

namespace DarkwoodMP.World.Loading
{
    /// <summary>
    /// The game set its bar: while a client joins, JoinScreen's bar (download
    /// first, then this) wins. It runs with no session too, as JoinScreen
    /// starts before the session does.
    /// </summary>
    [HarmonyPatch(typeof(WorldGenerator), "set_percentLoaded")]
    static class WorldGeneratorSetPercentLoaded
    {
        static void Postfix() => Plugin.Instance.Host.Join.OnGameBar();
    }
}
