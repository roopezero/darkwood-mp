using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.KeptCharacters
{
    /// The host's decisions about the characters it keeps for the players
    /// who join its world (Characters).
    public static class CharacterRules
    {
        /// The NewCharacters setting that gives someone new a copy of the
        /// host's character.
        public const string Copy = "copy";

        /// The skill whose generators use half the fuel.
        public const string ElectricianSkill = "electrician";

        /// A character message is taken (OnRequest, OnSave, OnDead,
        /// Electrician): on the host, from a peer with a player key.
        public static bool Takes(bool host, bool keyed) => host && keyed;

        /// Which character a joiner gets, first that applies: dead, from
        /// another chapter, their own, a copy, new.
        public static CharacterKind KindOf(in KindFacts facts)
        {
            if (facts.Kept && facts.Dead)
            {
                return CharacterKind.Dead;
            }

            if (facts.Kept && OtherChapter(facts.Chapter, facts.HostChapter))
            {
                return CharacterKind.OwnBesideHost;
            }

            if (facts.Kept)
            {
                return CharacterKind.Own;
            }

            if (facts.NewCharacters == Copy)
            {
                return CharacterKind.HostCopy;
            }

            return CharacterKind.New;
        }

        /// A record is from another chapter than the host's: any different
        /// chapter, later ones too, and never when either is 0.
        public static bool OtherChapter(int kept, int host) =>
            kept != 0 && host != 0 && kept != host;

        /// The chapter a CharacterSave is stamped with: the one it names,
        /// or the host's when it names none (0 or less).
        public static int ChapterOf(int sent, int host)
        {
            if (sent > 0)
            {
                return sent;
            }

            return host;
        }

        /// A CharacterSave is kept: the player and inventory strings aren't
        /// empty (an empty hotbar is fine). The shell's strings are never
        /// null.
        public static bool Keeps(string player, string inventory) =>
            player.Length != 0 && inventory.Length != 0;

        /// The host writes the kept characters when its game saves
        /// (HostSaved): never for a snapshot or on a client. Checked before
        /// the records are looked up, which would point them at the
        /// snapshot's folder.
        public static bool Writes(bool snapshotting, bool client) =>
            !snapshotting && !client;

        /// Then only when there are records and a folder for them.
        public static bool HasToWrite(int records, bool folder) =>
            records != 0 && folder;

        /// The records are read again (Records): a folder is known and it
        /// isn't the one they came from.
        public static bool Rereads(string folder, string loaded) =>
            folder != null && folder != loaded;

        /// How many items of these types a bag holds (Count): each item of
        /// one of them counts its amount, at least 1.
        public static int CountOf(
            IReadOnlyList<CarriedItem> bag,
            ICollection<string> types)
        {
            if (bag == null)
            {
                return 0;
            }

            var n = 0;
            for (int i = 0; i < bag.Count; i++)
            {
                var item = bag[i];
                if (item.Type != null && types.Contains(item.Type))
                {
                    n += Math.Max(1, item.Amount);
                }
            }

            return n;
        }

        /// What a client carries (Carries): its bags' counts less what it
        /// placed since. It can go below zero.
        public static int Carries(int inventory, int hotbar, int placed) =>
            inventory + hotbar - placed;

        /// Whether a character has the electrician skill: null when its
        /// skill list isn't known; a null entry is a skill without a type.
        public static bool? Electrician(IReadOnlyList<string> skills)
        {
            if (skills == null)
            {
                return null;
            }

            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] != null && skills[i] == ElectricianSkill)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
