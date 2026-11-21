namespace DarkwoodMP.Combat.Joiners
{
    /// The host's stand-ins for remote players (the plugin's Proxies and
    /// NetPlayerProxy): which exist, where their players are, and what
    /// they pass on.
    public static class ProxyRules
    {
        /// How far round a stand-in loose things stop colliding with it:
        /// more than a stand-in moves in a frame, and its own size.
        public const float LooseRadius = 150f;

        /// The most colliders one look round a stand-in finds.
        public const int MaxLoose = 128;

        /// A stand-in's collider radius when the host's player has no
        /// capsule, sphere or box collider to copy.
        public const float BodyRadius = 15f;

        /// A remote player's place in a dream starts with this.
        public const string DreamPrefix = "dream:";

        /// Stand-ins are wanted while hosting, or for the test dummy.
        public static bool Wanted(bool hosting, bool dummy) =>
            hosting || dummy;

        /// Whether a stand-in stays: not destroyed, still wanted, its
        /// player still there, and hosting unless it is the dummy's.
        public static bool Keeps(in KeepFacts proxy) =>
            proxy.Exists
            && proxy.Wanted
            && proxy.Known
            && (proxy.Hosting || proxy.Dummy);

        /// Whether the host places stand-ins this frame, after removing
        /// those it doesn't keep: they are wanted and the host has a
        /// player.
        public static bool Places(bool wanted, bool player) =>
            wanted && player;

        /// Whether a remote player gets a stand-in placed (and made if it
        /// has none): their puppet has a root, and hosting unless it is
        /// the dummy.
        public static bool Stands(bool root, bool hosting, bool dummy) =>
            root && (hosting || dummy);

        /// Where a stand-in's player is. Away from the host's world, a
        /// player in a location the host runs for them is still here.
        public static Presence PresenceOf(in PresenceFacts player)
        {
            bool inLocation = player.Hosting
                && player.Away
                && player.HasSnapshot
                && player.HostRuns;
            bool away = player.Away && !inLocation;
            return new Presence
            {
                Away = away,
                InLocation = inLocation,
                Here = player.Alive && !away,
            };
        }

        /// Which line the host logs when the presence changes.
        public static PresenceNote NoteOf(Presence presence)
        {
            if (presence.Away)
            {
                return PresenceNote.Away;
            }

            if (presence.InLocation)
            {
                return PresenceNote.InLocation;
            }

            return PresenceNote.Back;
        }

        /// Whether an away player is in a dream ("a dream") rather than a
        /// location the host hasn't got. Throws on a snapshot with no
        /// place, as the shell's StartsWith does.
        public static bool InDream(bool snapshot, string place) =>
            snapshot && place.StartsWith(DreamPrefix);

        /// Whether the player just turned invisible, so every character
        /// attacking their stand-in stops.
        public static bool TurnedInvisible(bool now, bool before) =>
            now && !before;

        /// Whether a hit on a stand-in goes to its player
        /// (PlayerDamage.Send): not when another player dealt it, as there
        /// is no friendly fire.
        public static bool PassesHit(bool byPlayer) => !byPlayer;

        /// Whether a stand-in stops colliding with a collider near it: a
        /// loose thing (an Item's rigidbody, not a character's, not its
        /// own) it doesn't ignore yet.
        public static bool IgnoresLoose(in LooseFacts thing) =>
            thing.Exists
            && !thing.Ignored
            && thing.HasBody
            && !thing.OwnBody
            && thing.Item
            && !thing.Character;

        /// Whether a stand-in stops colliding with one of a collider's
        /// parts (Ignore, our player's parts, a dragged thing's): it
        /// exists and isn't a trigger. Ignore also needs the stand-in's
        /// own collider and a collider it doesn't ignore yet.
        public static bool IgnoresPart(bool exists, bool trigger) =>
            exists && !trigger;
    }
}
