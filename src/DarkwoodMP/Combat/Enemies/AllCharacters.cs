using System.Collections.Generic;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The characters that are active now, as
    /// Object.FindObjectsOfType&lt;Character&gt;() finds them, without
    /// searching every object in the scene (that took about 50 ms in a loaded
    /// world, twice a second on the host). Every character passes
    /// Character.OnEnable (no kind of character has its own), which lists it
    /// here; the destroyed ones drop out and the inactive ones are skipped.
    /// It lives as long as the game (ModHost.Characters): characters enabled
    /// before a session starts are listed too.
    /// </summary>
    internal sealed class AllCharacters
    {
        private readonly HashSet<Character> _all = new HashSet<Character>();
        private readonly List<Character> _now = new List<Character>();

        internal void Add(Character c)
        {
            if (c != null)
            {
                _all.Add(c);
            }
        }

        /// <summary>
        /// A list reused on each call: use it before calling again.
        /// </summary>
        public List<Character> Active()
        {
            _now.Clear();
            _all.RemoveWhere(c => c == null);
            foreach (var c in _all)
            {
                if (c.isActiveAndEnabled)
                {
                    _now.Add(c);
                }
            }

            return _now;
        }
    }

    /// <summary>
    /// Lists each character as it's enabled (AllCharacters). Runs with no
    /// session too.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Character), "OnEnable")]
    static class CharacterOnEnable
    {
        static void Postfix(Character __instance) =>
            Plugin.Instance.Host.Characters.Add(__instance);
    }
}
