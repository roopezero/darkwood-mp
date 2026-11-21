namespace DarkwoodMP.Combat.Enemies
{
    /// Client: which character shows each of the host's enemies, and what
    /// the host's messages about one do to it (the plugin's
    /// NetEnemies.OnSpawn, OnDespawn, OnSound, OnBlood, ClientHit and
    /// SetArmor).
    public static class CopyRules
    {
        /// How long after our own hit the host's blood for that enemy
        /// isn't shown again (Time.realtimeSinceStartup).
        public const float PredictedBlood = 1f;

        /// The blood splat shown for the host's enemy in water.
        public const string WaterBlood = "FX/Bloodsplats/Shotsplat";

        /// The blood splat shown anywhere else, and for our own hit.
        public const string Blood = "FX/Bloodsplats/Shotsplat_stay";

        /// Whether our copy from the save shows the host's enemy: only a
        /// save id from the save both games loaded, of a character not
        /// driven by another id, of the same kind.
        public static SavedCopy FromSave(in SavedFacts saved)
        {
            if (saved.SaveId <= 0
                || saved.SaveId >= saved.Limit
                || !saved.Found
                || !saved.HasCharacter)
            {
                return SavedCopy.None;
            }

            if (saved.Driven)
            {
                return SavedCopy.Driven;
            }

            if (saved.Kind != saved.HostKind)
            {
                return SavedCopy.OtherKind;
            }

            return SavedCopy.Use;
        }

        /// The resources tried in turn, when no character of ours shows
        /// it: the host's prefab path, then the kind under Characters.
        public static string[] Prefabs(string prefab, string kind)
        {
            return new[]
            {
                "Prefabs/" + prefab,
                "Prefabs/Characters/" + kind,
            };
        }

        /// What an EnemyDespawn does, in the client's order of checks.
        public static DespawnStep Despawn(in DespawnFacts d)
        {
            if (!d.Copy)
            {
                if (d.KeepCorpse || !d.Corpse)
                {
                    return DespawnStep.Ignore;
                }

                if (!d.Exists)
                {
                    return DespawnStep.ForgetCorpse;
                }

                return DespawnStep.DestroyCorpse;
            }

            if (!d.Exists)
            {
                return DespawnStep.ForgetCopy;
            }

            if (d.KeepCorpse)
            {
                return DespawnStep.KeepCorpse;
            }

            return DespawnStep.Release;
        }

        /// A released copy that isn't ours to run again: one made here is
        /// destroyed, our copy from the save hidden.
        public static ReleaseStep Release(bool madeHere)
        {
            if (madeHere)
            {
                return ReleaseStep.Destroy;
            }

            return ReleaseStep.Hide;
        }

        /// What an EnemySound does. Once a copy is dead here only its
        /// death cry plays (and none without a CharacterSounds); "" stops
        /// its loop; one of its loops replaces the copy's.
        public static SoundStep Sound(in SoundFacts s)
        {
            if (!s.Copy)
            {
                return SoundStep.Ignore;
            }

            if (s.Dead && (!s.Sounds || !s.DeathCry))
            {
                return SoundStep.Ignore;
            }

            if (s.Empty)
            {
                if (s.Sounds)
                {
                    return SoundStep.StopLoop;
                }

                return SoundStep.Ignore;
            }

            if (s.IsLoop)
            {
                return SoundStep.Loop;
            }

            return SoundStep.Play;
        }

        /// The host's blood for an enemy is shown, unless our own hit on it
        /// showed a splat under PredictedBlood ago (that one is then used
        /// up). An older prediction stays and the blood is shown.
        public static bool ShowsBlood(bool predicted, Seconds since)
        {
            return !(predicted && since.Value < PredictedBlood);
        }

        /// The splat for the host's blood.
        public static string BloodFor(bool water)
        {
            if (water)
            {
                return WaterBlood;
            }

            return Blood;
        }

        /// Our own hit on a copy shows its health bar at once when it did
        /// damage.
        public static bool ShowsBar(float damage)
        {
            return damage > 0f;
        }

        /// A copy's shadow armour, to match the host's: put on when it
        /// has none, taken off when it has one.
        public static ArmorStep Armor(bool on, bool has, bool started)
        {
            if (on && !has)
            {
                return ArmorStep.Add;
            }

            if (!on && has)
            {
                if (!started)
                {
                    return ArmorStep.Destroy;
                }

                return ArmorStep.Die;
            }

            return ArmorStep.None;
        }
    }
}
