namespace DarkwoodMP.Combat.Cues
{
    /// A client's swing and aim, sent to the host so that its enemies
    /// dodge or get scared as they would for the host's player (the
    /// plugin's PlayerCues).
    public static class CueRules
    {
        /// The kind byte of a swing (after notifyEnemiesAboutAttack).
        public const byte Dodge = 0;

        /// The kind byte of an aim (before any scareInArea on a client).
        public const byte Scare = 1;

        /// How far round the stand-in characters may dodge a swing.
        public const float DodgeRadius = 150f;

        /// How far round the stand-in characters are scared by an aim.
        public const float ScareRadius = 350f;

        /// The game's layer mask in both of its calls.
        public const int EnemyLayers = 2099200;

        /// The most colliders one look round the stand-in finds. A
        /// character with two colliders there is told twice.
        public const int MaxNear = 128;

        /// Whether this game sends its player's cues: a client that runs
        /// frozen copies of the host's enemies (NetEnemies.ClientMode).
        public static bool Sends(bool session, bool client, bool copies) =>
            session && client && copies;

        /// What the host does on a cue: nothing unless it hosts and the
        /// sender's stand-in is alive; then a swing makes them dodge and
        /// an aim scares them; another kind does nothing. The kind is
        /// read only once it hosts.
        public static CueReaction Reaction(in CueFacts cue)
        {
            if (!cue.Hosting || !cue.StandInAlive)
            {
                return CueReaction.None;
            }

            if (cue.Kind == Dodge)
            {
                return CueReaction.Dodge;
            }

            if (cue.Kind == Scare)
            {
                return CueReaction.Scare;
            }

            return CueReaction.None;
        }

        /// Whether a character on a client dodges or gets scared: not a
        /// frozen copy of the host's enemy (NetEnemies.Frozen) while the
        /// client runs copies.
        public static bool Reacts(bool copies, bool frozen) =>
            !(copies && frozen);
    }
}
