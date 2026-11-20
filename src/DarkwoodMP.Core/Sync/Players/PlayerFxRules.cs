namespace DarkwoodMP.Sync.Players
{
    /// Which of our player's sounds, shots, wounds and flames the others
    /// see and hear, and what this game does with theirs (PlayerFx and
    /// its patches).
    public static class PlayerFxRules
    {
        /// Seconds (unscaled) between two batches of our sounds.
        public const float SoundInterval = 0.1f;

        /// The most sounds in one batch, sent or taken.
        public const int MaxSounds = 16;

        /// Further than this from our player, their sounds aren't played.
        public const float HearingRange = 3000f;

        /// The loudest volume taken from a peer.
        public const float MaxVolume = 2f;

        /// How far above or below their puppet a blood splat is shown.
        public const float MaxBloodHeight = 50f;

        /// The only prefabs a peer may make this game spawn as blood.
        public const string BloodPrefabs = "FX/Bloodsplats/";

        /// The flames of a burning player whose game names none.
        public const string DefaultFlames = "fire_flamesV2_burningEnemy_01";

        /// The fire sound of a burning player whose game names none.
        public const string DefaultFireSound = "fire_loop_01";

        /// Shared in a connected session with someone to share with (a
        /// client always has the host), in a world. While not sharing, the
        /// queued sounds are emptied.
        public static bool Sharing(in SharingFacts session)
        {
            if (!session.Active || !session.Connected)
            {
                return false;
            }

            if (!session.Client && session.Peers <= 0)
            {
                return false;
            }

            return session.InWorld;
        }

        /// A sound of ours is queued while the batch has room; past
        /// MaxSounds it's dropped.
        public static bool Queues(int queued) => queued < MaxSounds;

        /// The batch goes when it holds a sound and SoundInterval has
        /// passed since the last.
        public static bool SendsSounds(int queued, Seconds now, Seconds next)
        {
            if (queued <= 0)
            {
                return false;
            }

            return now.Value >= next.Value;
        }

        /// When the next batch may go, after one at now.
        public static Seconds NextSounds(Seconds now) =>
            new Seconds(now.Value + SoundInterval);

        /// How many of the queued sounds one batch holds.
        public static int BatchSize(int queued)
        {
            if (queued < MaxSounds)
            {
                return queued;
            }

            return MaxSounds;
        }

        /// Our player burns: alive and with a Burn on it. A dead player
        /// counts as not burning.
        public static bool Burning(bool alive, bool hasBurn) =>
            alive && hasBurn;

        /// A burning message is sent when burning differs from what was
        /// sent last. A newcomer (OnPeerConnected) resets what was sent to
        /// not burning, so only a burning player sends again.
        public static bool SendsBurning(bool burning, bool sent) =>
            burning != sent;

        /// The flames a burning player's puppet shows: none when the Burn
        /// has no particles, else the player's burn prefab, else
        /// DefaultFlames. prefab is the burn prefab's name, null without
        /// one.
        public static string BurnFlames(bool noParticle, string prefab)
        {
            if (noParticle)
            {
                return "";
            }

            if (prefab != null)
            {
                return prefab;
            }

            return DefaultFlames;
        }

        /// The fire sound: none when the Burn has no sound, else its
        /// override, else DefaultFireSound.
        public static string BurnSound(bool noSound, string sound)
        {
            if (noSound)
            {
                return "";
            }

            if (string.IsNullOrEmpty(sound))
            {
                return DefaultFireSound;
            }

            return sound;
        }

        /// Whom an effect message is about: the sender on the host (never
        /// the owner a client says); on a client the owner it says, from
        /// the host and not about us, else 0.
        public static ulong Owner(in OwnerFacts message)
        {
            if (message.Host)
            {
                return message.From;
            }

            if (message.From == message.HostId
                && message.Said != message.LocalId)
            {
                return message.Said;
            }

            return 0;
        }

        /// A batch of sounds is dropped with no owner or more than
        /// MaxSounds, in one check.
        public static FxDrop TakesSounds(ulong owner, int count)
        {
            if (owner == 0)
            {
                return FxDrop.NoOwner;
            }

            if (count > MaxSounds)
            {
                return FxDrop.TooManySounds;
            }

            return FxDrop.None;
        }

        /// A sound's volume, held to 0 to MaxVolume: what is played and
        /// what is relayed. NaN stays NaN, as Mathf.Clamp leaves it.
        public static float Volume(float volume) =>
            GameMath.Clamp(volume, 0f, MaxVolume);

        /// Their sounds play here with their puppet shown, in a world, with
        /// our player there, and our player nearer the puppet than
        /// HearingRange (in three dimensions, strictly).
        public static bool Hears(in HearFacts hear)
        {
            if (!hear.HasPuppet || !hear.InWorld || !hear.HasPlayer)
            {
                return false;
            }

            var apart = new Vec3(
                hear.Ours.X - hear.Puppet.X,
                hear.Ours.Y - hear.Puppet.Y,
                hear.Ours.Z - hear.Puppet.Z);
            float range = HearingRange * HearingRange;
            return GameMath.LengthSquared(apart) < range;
        }

        /// A shot is dropped with no owner. Otherwise it's relayed before
        /// the puppet is looked for.
        public static FxDrop TakesShot(ulong owner)
        {
            if (owner == 0)
            {
                return FxDrop.NoOwner;
            }

            return FxDrop.None;
        }

        /// A hurt message of an unknown kind is dropped (and not relayed),
        /// before the owner is checked.
        public static FxDrop TakesHurt(byte kind, ulong owner)
        {
            if (kind != (byte)HurtKind.Blood
                && kind != (byte)HurtKind.Burning
                && kind != (byte)HurtKind.Stopped)
            {
                return FxDrop.UnknownKind;
            }

            if (owner == 0)
            {
                return FxDrop.NoOwner;
            }

            return FxDrop.None;
        }

        /// Blood is relayed unreliably; burning and stopping, reliably.
        public static bool RelaysReliably(HurtKind kind) =>
            kind != HurtKind.Blood;

        /// A peer doesn't get to spawn any prefab it names: blood is shown
        /// only for a prefab under BloodPrefabs. A refused one is still
        /// relayed.
        public static bool ShowsBlood(string prefab) =>
            prefab.StartsWith(BloodPrefabs);

        /// How high above their puppet the blood is shown, held to
        /// MaxBloodHeight either way. Only the shown splat is held; the
        /// relay sends the height as it came.
        public static float BloodHeight(float height) =>
            GameMath.Clamp(height, -MaxBloodHeight, MaxBloodHeight);

        /// A sound is our player's: not while replaying another game's
        /// change, nor with no player. With a parent, the parent is our
        /// player or under it. Without one, it was played from our
        /// player's own code, and not inside a shared world action, whose
        /// sounds come from its replay.
        public static bool IsOurs(in SoundSourceFacts sound)
        {
            if (!sound.HasPlayer || sound.Replaying)
            {
                return false;
            }

            if (sound.HasParent)
            {
                return sound.ParentIsOurs;
            }

            return sound.InPlayerCode > 0 && sound.Depth == 0;
        }

        /// Only sounds that don't loop are sent: a sound with an audio item
        /// whose loop mode is DoNotLoop.
        public static bool SendsSound(bool hasItem, bool loops) =>
            hasItem && !loops;

        /// A shot is our player's: fired by our own Player, while sharing,
        /// with a gun (currentItem.baseClass) in hand.
        public static bool ShotIsOurs(bool ourPlayer, bool sharing, bool gun)
        {
            if (!ourPlayer || !sharing)
            {
                return false;
            }

            return gun;
        }

        /// A prefab is our player's blood: added inside Player.getHit, a
        /// prefab under BloodPrefabs, while sharing and not replaying.
        public static bool BloodIsOurs(in BloodFacts blood)
        {
            if (blood.InGetHit == 0 || !blood.HasResult)
            {
                return false;
            }

            if (blood.Prefab == null || !blood.Prefab.StartsWith(BloodPrefabs))
            {
                return false;
            }

            return blood.Sharing && !blood.Replaying;
        }

        /// The depth counters (InPlayerCode, InGetHit) after a patched
        /// call ends: one less, never below 0.
        public static int Leave(int depth)
        {
            if (depth > 0)
            {
                return depth - 1;
            }

            return depth;
        }
    }
}
