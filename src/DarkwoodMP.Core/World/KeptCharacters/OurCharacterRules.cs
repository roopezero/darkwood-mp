using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.KeptCharacters
{
    /// A client's decisions about its own character in the host's world
    /// (Characters' client half).
    public static class OurCharacterRules
    {
        /// How long a joiner's game waits for its character, from when the
        /// world became ready, before it shows its player anyway.
        public static readonly Seconds PlaceWait = new Seconds(6f);

        /// How often a client sends its character back.
        public static readonly Seconds SaveInterval = new Seconds(15f);

        /// A chunk counts within this share of a chunk's size.
        public const float OnMapShare = 0.6f;

        /// Each upgrade taken back costs this much max health or stamina.
        public const float UpgradeCost = 25f;

        /// A free spot this close (squared) to where we were put is no move.
        public const float MinMoveSquared = 1f;

        /// Unity's Vector3 == compares the squared distance with this.
        public const float ZeroSquared = 9.99999944E-11f;

        /// Our player is still being placed (Characters.Placing), so
        /// PlayerSync holds back where it is: in the host's world as a
        /// client, without our character, until PlaceWait after the world
        /// became ready (or while it isn't).
        public static bool Placing(in OurFacts facts)
        {
            if (!facts.Client || facts.Have || !facts.InWorld)
            {
                return false;
            }

            return facts.ReadyAt.Value < 0f
                || facts.Now - facts.ReadyAt < PlaceWait;
        }

        /// We send our character now (SaveNow): not after the quit's save,
        /// before it came, out of client mode or a world, or in the
        /// prologue, whose character isn't kept.
        public static bool Sends(in OurFacts facts) =>
            !facts.QuitSent
                && facts.Have
                && facts.Client
                && facts.InWorld
                && !facts.Prologue;

        /// What to do with a CharacterLoad (OnLoad), first that applies.
        public static LoadStep Take(in LoadFacts facts)
        {
            if (!facts.FromHost || !facts.Client)
            {
                return LoadStep.Ignore;
            }

            if (!facts.Asked
                || facts.Scene != facts.AskedScene
                || !facts.InWorld)
            {
                return LoadStep.AskAgain;
            }

            return LoadStep.Take;
        }

        /// The record is applied to our player for our own kinds.
        public static bool Applies(CharacterKind kind) =>
            kind == CharacterKind.Own || kind == CharacterKind.OwnBesideHost;

        /// In the prologue a new character, or a copy, is what the stage
        /// made it: only put beside the host.
        public static bool InPrologue(in StandFacts facts) =>
            facts.Prologue
                && (facts.Kind == CharacterKind.New
                    || facts.Kind == CharacterKind.HostCopy);

        /// Our own kept place isn't on this map: beside the host. Never
        /// with a zero spawn.
        public static bool OffMap(in StandFacts facts) =>
            !InPrologue(facts)
                && facts.Kind == CharacterKind.Own
                && !IsZero(facts.Spawn)
                && !facts.OnMap;

        /// Where our character stands, first that applies: the prologue's
        /// at a non-zero spawn, another chapter's or an off-map one at a
        /// non-zero spawn, a new one made fresh, a copy at a non-zero spawn.
        public static StandStep Stand(in StandFacts facts)
        {
            var spawn = !IsZero(facts.Spawn);
            if (InPrologue(facts))
            {
                return At(spawn);
            }

            if (facts.Kind == CharacterKind.OwnBesideHost || OffMap(facts))
            {
                if (spawn)
                {
                    return StandStep.AtSpawn;
                }
            }

            if (facts.Kind == CharacterKind.New)
            {
                return StandStep.Fresh;
            }

            if (facts.Kind == CharacterKind.HostCopy)
            {
                return At(spawn);
            }

            return StandStep.Stay;
        }

        /// The line logged once placed.
        public static LoadNote Note(in StandFacts facts)
        {
            if (InPrologue(facts))
            {
                return LoadNote.Prologue;
            }

            switch (facts.Kind)
            {
                case CharacterKind.Own:
                    return LoadNote.Own;
                case CharacterKind.OwnBesideHost:
                    return LoadNote.LastChapter;
                case CharacterKind.New:
                    return LoadNote.New;
                default:
                    return LoadNote.HostCopy;
            }
        }

        /// Unity's Vector3 == Vector3.zero: the squared length is below
        /// ZeroSquared.
        public static bool IsZero(Vec3 v) =>
            v.X * v.X + v.Y * v.Y + v.Z * v.Z < ZeroSquared;

        /// How far the nearest chunk is (NearestChunk), by the larger of
        /// the x and z distances; 0 when not known (no chunks, or a size of
        /// 0 or less).
        public static float NearestChunk(
            IReadOnlyList<Vec2> chunks,
            float size,
            Vec2 at)
        {
            if (chunks == null || chunks.Count == 0 || size <= 0f)
            {
                return 0f;
            }

            var best = float.MaxValue;
            for (int i = 0; i < chunks.Count; i++)
            {
                var dx = Math.Abs(chunks[i].X - at.X);
                var dz = Math.Abs(chunks[i].Z - at.Z);
                best = Math.Min(best, Math.Max(dx, dz));
            }

            if (best == float.MaxValue)
            {
                return 0f;
            }

            return best;
        }

        /// A place is on the map (OnMap): within OnMapShare of a chunk's
        /// size of a chunk. True when that can't be told; false for a size
        /// below 0, as 0 is never at most a negative share.
        public static bool OnMap(
            IReadOnlyList<Vec2> chunks,
            float size,
            Vec2 at) =>
            NearestChunk(chunks, size, at) <= OnMapShare * size;

        /// An upgrade count from our record over the one our player has
        /// (Delta): how many to give loadValues, and how many our player
        /// gives back first.
        public static int UpgradesToAdd(int wanted, int have) =>
            Math.Max(0, wanted - have);

        public static int UpgradesToTakeBack(int wanted, int have) =>
            Math.Max(0, have - wanted);

        /// The health we load with: the record's, at most our max health.
        public static float Health(float kept, float max) =>
            Math.Min(kept, max);

        /// Slots to add (above 0) or remove (below 0) for a bag to hold the
        /// record's count (SetSlots); none for a count of 0 or less.
        public static int SlotChange(int have, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return count - have;
        }

        /// The free spot is a move (PlaceFree): at least 1 unit away.
        public static bool Moves(Vec3 want, Vec3 free)
        {
            var dx = free.X - want.X;
            var dy = free.Y - want.Y;
            var dz = free.Z - want.Z;
            return !(dx * dx + dy * dy + dz * dz < MinMoveSquared);
        }

        static StandStep At(bool spawn)
        {
            if (spawn)
            {
                return StandStep.AtSpawn;
            }

            return StandStep.Stay;
        }
    }
}
