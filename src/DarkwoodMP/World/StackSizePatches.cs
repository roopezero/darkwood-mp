using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The items database is made with each world (and the main menu's): set
    /// the sizes on it (StackSizes). This runs in a game played alone too.
    /// </summary>
    [HarmonyPatch(typeof(ItemsDatabase), "Awake")]
    static class ItemsDatabaseAwake
    {
        static void Postfix()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null && plugin.Host != null)
            {
                plugin.Host.Stacks.Apply(StackSizes.Current);
            }
        }
    }
}
