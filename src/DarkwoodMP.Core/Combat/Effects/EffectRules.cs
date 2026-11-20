namespace DarkwoodMP.Combat.Effects
{
    /// What the clients see and hear of the host's enemies that their own
    /// copies don't make: combat music, effect prefabs and emitters
    /// (EnemyFx), and EnemyExplosion's flash (EnemyAreaHits.OnExplosion).
    public static class EffectRules
    {
        /// How often EnemyFx.Update runs, in unscaled time.
        public const float Every = 0.5f;

        /// How far from our player the host's effects are shown.
        public const float ShowRange = 4000f;

        /// The kind of an EnemyEffect with a rotation (a prefab).
        public const byte PrefabKind = 1;

        /// The kind of an EnemyEffect from an effect pool.
        public const byte PooledKind = 2;

        /// The kind of an EnemyEffect switching an enemy's emitter.
        public const byte EmitterKind = 3;

        /// The pool the host's pooled effects come from and go to.
        public const string Pool = "FX";

        /// The only pooled effect the host sends.
        public const string ForestSpirit = "ForestSpirit_fastSpawnEff";

        /// The banshee's scream, sent as a prefab.
        public const string BansheeScream = "FX/BansheeScream";

        /// Whether the host's side runs: we host, with peers, in the
        /// world (WorldShare.InWorld).
        public static bool Hosting(bool host, int peers, bool inWorld) =>
            host && peers > 0 && inWorld;

        /// Whether the client's side (the banshee's sight) runs on a
        /// tick: NetEnemies.ClientMode, in the world.
        public static bool Client(bool client, bool inWorld) =>
            client && inWorld;

        /// The tick of EnemyFx.Update: due every Every seconds. The host's
        /// part and the client's run in the same tick.
        public static Tick Due(Seconds now, Seconds next) =>
            Tick.At(now, next, new Seconds(Every));

        /// Whether one character makes a joiner be in combat: alive, one
        /// of the host's enemies, chasing, and its target is their
        /// stand-in. A dead stand-in is never in combat (the shell checks
        /// that before any character).
        public static bool Chases(in ChaserFacts c) =>
            c.Alive && c.Enemy && c.Chasing && c.OnStandIn;

        /// The host sends a joiner's combat state only when it changes;
        /// the dummy's is kept but never sent.
        public static CombatChange Change(bool fight, bool was, bool dummy)
        {
            if (fight == was)
            {
                return CombatChange.Same;
            }

            if (dummy)
            {
                return CombatChange.Kept;
            }

            return CombatChange.Sent;
        }

        /// Client: whether the combat music should play: the host says an
        /// enemy is after us, or the game's charactersAttackingMe has any
        /// (0 when it is null). InCombat is set only when it differs.
        public static bool Music(bool hostSays, int attackers) =>
            hostSays || attackers > 0;

        /// Client: whether Player.checkInCombatChars is skipped, with
        /// InCombat kept on: the host says so, in ClientMode.
        public static bool KeepsMusic(bool hostSays, bool client) =>
            hostSays && client;

        /// Host: whether an effect goes out: hosting, and not a replay of
        /// another game's.
        public static bool Sends(bool hosting, bool replaying) =>
            hosting && !replaying;

        /// Whether an EnemyEffect of this kind carries a rotation.
        public static bool Rotates(byte kind) => kind == PrefabKind;

        /// Whether an enemy's emitter change goes out: one of the host's
        /// enemies with a particle light or a particle emitter. The peers
        /// that see it (NetEnemies.PeersSeeing) get it, when it has an ID.
        public static bool SpreadsEmitter(
            bool enemy, bool light, bool emitter) =>
            enemy && (light || emitter);

        /// Whether a pooled effect goes out: the call made one, from the
        /// "FX" pool, and it is the forest spirit's (whoever made it).
        public static bool SpreadsPooled(
            bool made, string pool, string prefab) =>
            made && pool == Pool && prefab == ForestSpirit;

        /// Client: whether the host's effect or explosion is shown: our
        /// player exists, isn't away (EnemyAreaHits.PlayerAway) and is
        /// within ShowRange of it.
        public static bool Shown(bool player, bool away, float d) =>
            player && !away && !(d > ShowRange);

        /// Client: what an EnemyEffect does. An emitter is checked first,
        /// before any distance; then not shown; a prefab for kind 1, and
        /// any other kind is pooled.
        public static EffectShow Show(byte kind, bool shown)
        {
            if (kind == EmitterKind)
            {
                return EffectShow.Emitter;
            }

            if (!shown)
            {
                return EffectShow.NotShown;
            }

            if (kind == PrefabKind)
            {
                return EffectShow.Prefab;
            }

            return EffectShow.Pooled;
        }
    }
}
