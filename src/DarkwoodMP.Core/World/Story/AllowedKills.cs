using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// Host: the NPCs whose death a joiner's NpcDied request showed, and
    /// when (unscaled time). An entry stays until the session ends: one
    /// allowed kill lets any number of dead states through for the window.
    public sealed class AllowedKills
    {
        readonly Dictionary<string, Seconds> _at =
            new Dictionary<string, Seconds>();

        /// A joiner killed this NPC and the host checked it. A null or
        /// empty name is left out.
        public void Allow(string npc, Seconds now)
        {
            if (string.IsNullOrEmpty(npc))
            {
                return;
            }

            _at[npc] = now;
        }

        /// Whether a kill of this NPC was allowed less than
        /// NpcStateRules.KillWindow ago. A null name throws, as the
        /// dictionary does.
        public bool Recent(string npc, Seconds now)
        {
            if (!_at.TryGetValue(npc, out var at))
            {
                return false;
            }

            return now.Value - at.Value < NpcStateRules.KillWindow;
        }

        /// The session ended.
        public void Clear()
        {
            _at.Clear();
        }
    }
}
