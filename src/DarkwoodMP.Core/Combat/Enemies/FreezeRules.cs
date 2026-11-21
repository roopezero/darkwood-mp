namespace DarkwoodMP.Combat.Enemies
{
    /// Client: which of its own characters it freezes, so that only the
    /// host runs them, and what a frozen one or a copy of the host's
    /// enemy may still do (the plugin's NetEnemies.ClientMode, LocalOnly,
    /// Freeze, ClientSweep and IsCopy, and the client patches on
    /// Character.Start, enableComponents and heardSound,
    /// CharacterSounds.playIdleLoop and CharacterSpawnPoint.actuallySpawn).
    /// A frozen copy never acts: these say where the game's own code is
    /// kept from running on one.
    public static class FreezeRules
    {
        /// How often the client sweeps its characters for ones to freeze.
        public const float SweepInterval = 1f;

        /// How far from our player a thing in the host's world still
        /// counts as only ours, in 3D, where the host doesn't run the
        /// area.
        public const float LocalRange = 4000f;

        /// A client in the host's world: its own enemies don't run, the
        /// host's are shown.
        public static bool ClientMode(in ClientFacts client)
        {
            return client.HasSession
                && client.Client
                && client.Connected
                && client.Redirected;
        }

        /// A message about the host's enemies is taken: it came from the
        /// host, to a client in the host's world. EnemyDespawn and
        /// EnemySound check only the sender.
        public static bool FromHost(bool host, bool clientMode)
        {
            return host && clientMode;
        }

        /// A thing in an area only this player is in, which runs here as
        /// in single player: a scene of its own, this game's own dream,
        /// or near our player where the host doesn't run the area.
        public static bool LocalOnly(in LocalFacts thing)
        {
            if (!thing.Exists)
            {
                return false;
            }

            if (!thing.ActiveScene)
            {
                return true;
            }

            if (thing.Dreaming && thing.InDream && thing.OwnDream)
            {
                return true;
            }

            if (thing.HostRunsHere)
            {
                return false;
            }

            return thing.HasPlayer && Near(thing.Thing, thing.Us);
        }

        /// As a character starts: frozen instead of started (the game's
        /// Start is skipped). Hidden too unless it's a copy.
        public static bool FreezesAtStart(in StartFacts c)
        {
            return !c.Thawing && c.ClientMode && c.Eligible && !c.Local;
        }

        /// Freeze hides the character (and stops its sounds) when asked
        /// to, unless it's a corpse the host told us to keep.
        public static bool Hides(bool hide, bool keptCorpse)
        {
            return hide && !keptCorpse;
        }

        /// The sweep, for one active, enabled character of ours.
        public static SweepStep Sweep(in SweepFacts c)
        {
            if (!c.Eligible || c.Copy)
            {
                return SweepStep.None;
            }

            if (c.Local && c.Frozen)
            {
                return SweepStep.Thaw;
            }

            if (!c.Local && !c.Frozen)
            {
                return SweepStep.Freeze;
            }

            return SweepStep.None;
        }

        /// Character.enableComponents(enable) is skipped: a frozen one
        /// stays frozen as its area is switched on again.
        public static bool StaysFrozen(
            bool enable,
            bool clientMode,
            bool frozen)
        {
            return enable && clientMode && frozen;
        }

        /// CharacterSounds.playIdleLoop(loop) is skipped: a hidden one of
        /// ours starts no loop. Stopping a loop ("") still runs.
        public static bool StaysSilent(
            string loop,
            bool clientMode,
            bool hidden)
        {
            return !string.IsNullOrEmpty(loop) && clientMode && hidden;
        }

        /// CharacterSpawnPoint.actuallySpawn runs: not in the host's world,
        /// or the spawn point is in an area only we are in.
        public static bool SpawnsOwn(bool clientMode, bool local)
        {
            return !clientMode || local;
        }

        /// Character.heardSound runs: anything but a frozen one in the
        /// host's world.
        public static bool Hears(bool clientMode, bool frozen)
        {
            return !(clientMode && frozen);
        }

        /// A ShadowArmor is on a copy of the host's enemy or on a frozen
        /// one of ours, so its light check and damage do nothing: on any
        /// session's client (IsClient, not ClientMode).
        public static bool IsCopy(bool client, bool frozen, bool copy)
        {
            return client && (frozen || copy);
        }

        /// As Unity's (a - b).sqrMagnitude < range * range.
        static bool Near(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z < LocalRange * LocalRange;
        }
    }
}
