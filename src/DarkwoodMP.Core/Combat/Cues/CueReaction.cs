namespace DarkwoodMP.Combat.Cues
{
    /// What the host's enemies do on a client's cue.
    public enum CueReaction
    {
        /// Nothing: not the host, no living stand-in, or an unknown kind.
        None,

        /// Each character within CueRules.DodgeRadius may dodge
        /// (doIWantToDodge).
        Dodge,

        /// Characters within CueRules.ScareRadius are scared
        /// (Character.scareInArea).
        Scare,
    }
}
