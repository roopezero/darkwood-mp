using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Host: the save id counters of the joiners let in to the loaded world
    /// (see SharedIds), for one session (CoopSession.Joiners). They start
    /// again when the host's world changes.
    /// </summary>
    internal sealed class JoinerSaveIds
    {
        /// Over the joiners let in to the world loaded in its Scene.
        public JoinerIds Ids { get; private set; }

        private static int Scene => SceneManager.GetActiveScene().handle;

        /// <summary>
        /// A joiner was let in with a world whose counter is n.
        /// </summary>
        public void Joined(ulong peer, int n)
        {
            // joiners let in to an earlier world don't count here
            Ids = SharedIdRules.Joined(Ids, n, Scene);
            if (n > 0)
            {
                Plugin.Log.LogInfo(
                    $"#{peer} joined with save ids below {n}; save ids below "
                        + $"{Ids.Min} are shared with every player");
            }
        }

        /// <summary>
        /// The host's world changed, so the joiners' copies are stale until
        /// they load the new one.
        /// </summary>
        public void Reset()
        {
            Ids = SharedIdRules.Reset(Scene);
        }
    }
}
