using System;

namespace DarkwoodMP.World.Owed
{
    /// Who is still owed their own part of a once-only trigger that fired
    /// for someone else (the plugin's OwedParts): how a trigger is named,
    /// and when the host's record is read and written beside the save.
    public static class OwedRules
    {
        /// The record's file in the profile folder.
        public const string FileName = "darkwoodmp_later.json";

        /// A trigger's name: "s{save id}#{index}" when its object has a save
        /// id above 0, else "{scene}/{name}@{x},{z}#{index}" with x and z
        /// rounded ("0") in the invariant culture. On .NET 8 a value just
        /// below 0 rounds to "-0"; on the game's Mono it is "0".
        public static string IdOf(in TriggerNameFacts trigger)
        {
            if (trigger.Saved && trigger.SaveId > 0)
            {
                return $"s{trigger.SaveId}#{trigger.Index}";
            }

            float x = trigger.X;
            float z = trigger.Z;
            return FormattableString.Invariant(
                $"{trigger.Scene}/{trigger.Name}@{x:0},{z:0}#{trigger.Index}");
        }

        /// Whether our player is owed its own part: never without a trigger
        /// or an active session; the host when its record owes it; a joiner
        /// for a trigger of the shared world it hasn't had its part of here.
        public static bool Owed(in OwedFacts owed)
        {
            if (!owed.Trigger || !owed.Active)
            {
                return false;
            }

            if (owed.Host)
            {
                return owed.HostOwed;
            }

            if (!owed.HasObject || owed.OursAlone)
            {
                return false;
            }

            return !owed.HadHere;
        }

        /// Whether a player's record goes under their key's hash
        /// (Characters.KeyHash): the session knows a key for them that
        /// isn't empty (null when it knows none).
        public static bool UsesKey(string key)
        {
            return !string.IsNullOrEmpty(key);
        }

        /// The name a player's record goes under without a key.
        public static string Unkeyed(ulong peer)
        {
            return "#" + peer;
        }

        /// Whether the record is written as the game saves, in this order:
        /// not for a joiner's snapshot, not on a client, not before it was
        /// read; then not without a profile, for another profile than it
        /// was read from, or when it holds nothing.
        public static bool Writes(in RecordSaveFacts save)
        {
            if (save.Snapshotting || save.Client || !save.Loaded)
            {
                return false;
            }

            if (save.Profile == null || save.Profile != save.RecordProfile)
            {
                return false;
            }

            return !save.Empty;
        }

        /// What of the record is read before it is used: all of it the
        /// first time or for another profile, HostOwed alone at a new world.
        public static RecordRead Reads(in RecordReadFacts record)
        {
            if (!record.Loaded || record.Profile != record.RecordProfile)
            {
                return RecordRead.Whole;
            }

            if (record.Scene != record.OwedScene)
            {
                return RecordRead.HostOwed;
            }

            return RecordRead.None;
        }
    }
}
