using System;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The "journal updated" popup (Journal.showJournalInfoPopup) shows the
    /// game's text for what was found, looked up by its name. When the game has
    /// no text for that name, Language.Get gives back the bare name, and the
    /// popup would say something like "Location found: place_10". This happens
    /// for some small places in the game itself, and for a place, entry or note
    /// another player found (FlagsSync), so such a popup isn't shown at all;
    /// what was found is still added to the journal. It holds in a game played
    /// alone too.
    /// </summary>
    internal static class JournalPopups
    {
        /// <summary>
        /// Whether the popup of this type for this name has the game's text,
        /// so it is shown. Logs a popup that isn't.
        /// </summary>
        public static bool HasText(string type, string desc)
        {
            bool has;
            try
            {
                var text = JournalRules.TextOf(type, desc);
                if (text.Always)
                {
                    // reputation and others carry their own text
                    return true;
                }

                has = Has(text.Key, text.Sheet)
                    || (text.OtherSheet != null
                        && Has(text.Key, text.OtherSheet));
            }
            catch (Exception)
            {
                return true; // the language isn't loaded: leave it to the game
            }

            if (!has)
            {
                Plugin.Log.LogInfo(
                    $"Journal: no popup for {type} {desc}, the game has no "
                        + "text for it");
            }

            return has;
        }

        private static bool Has(string key, string sheet)
            => !string.IsNullOrEmpty(key)
                && global::Language.HasKey(key, sheet)
                && !string.IsNullOrEmpty(global::Language.Get(key, sheet));
    }
}
