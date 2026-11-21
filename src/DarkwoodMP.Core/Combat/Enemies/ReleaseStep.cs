namespace DarkwoodMP.Combat.Enemies
{
    /// What becomes of a copy the host stopped sending, once it isn't an
    /// NPC that is ours again (CopyRules.Release). Its window is closed
    /// first.
    public enum ReleaseStep
    {
        /// Made here from its prefab: destroyed.
        Destroy = 0,

        /// Our copy from the save: hidden and back in our culling.
        Hide,
    }
}
