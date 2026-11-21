using System.Collections.Generic;

namespace DarkwoodMP.World.Nights
{
    /// Objects a night event makes (the plugin's NightObjects): the host
    /// records what each tagged spawn step makes and sends it; a client
    /// makes the host's copy, or ties the one from its save to it.
    public static class NightObjectRules
    {
        /// A message kind: made now.
        public const byte Made = 1;

        /// A message kind: gone.
        public const byte Gone = 2;

        /// A message kind: made before the player came (sent on connect).
        public const byte MadeBefore = 3;

        /// A trap's state: it has no Trigger.
        public const byte NoTrap = 255;

        /// A trap's state: armed.
        public const byte Armed = 1;

        /// A trap's state: not armed.
        public const byte Unarmed = 0;

        /// Seconds between the host's checks of its recorded objects
        /// (unscaled).
        public const float CheckEvery = 1f;

        /// Seconds after an object is armed until it is armed again, once
        /// its own Start has run (unscaled).
        public const float RearmAfter = 1f;

        /// Whether the host records what is made: hosting, peers or not,
        /// so what is made before a player comes is sent when they come.
        public static bool Records(in NightHostFacts host)
        {
            if (!host.HasSession || !host.IsHost || host.Replaying)
            {
                return false;
            }

            return host.InWorld;
        }

        /// Whether the host sends Made and Gone as they happen.
        public static bool Sends(in NightHostFacts host)
        {
            return host.HasSession && host.IsHost && host.Sharing;
        }

        /// What the host does with an object Core.AddPrefab made. A night
        /// event's copy with no GameEvents is looked at as a step's.
        public static AddedStep Added(in AddedFacts made)
        {
            if (!made.Made || !made.Records)
            {
                return AddedStep.Nothing;
            }

            if (made.LocationEvent && made.InGroup && made.CopyHasEvents)
            {
                return AddedStep.Tag;
            }

            if (!made.HasStep || !made.SpawnStep)
            {
                return AddedStep.Nothing;
            }

            if (!made.TargetIsPrefab || !made.Tagged)
            {
                return AddedStep.Nothing;
            }

            if (made.Character && !made.HasNpc)
            {
                return AddedStep.Nothing;
            }

            return AddedStep.Record;
        }

        /// A trap's state from its Trigger.
        public static byte ArmedOf(bool trap, bool active)
        {
            if (!trap)
            {
                return NoTrap;
            }

            if (active)
            {
                return Armed;
            }

            return Unarmed;
        }

        /// Whether the host checks its recorded objects now.
        public static bool CheckDue(int live, Seconds now, Seconds next)
        {
            return live > 0 && !(now.Value < next.Value);
        }

        /// When the next check is due.
        public static Seconds NextCheck(Seconds now)
        {
            return new Seconds(now.Value + CheckEvery);
        }

        /// What the check does with one object. A gone one is dropped
        /// whether or not the host shares.
        public static LiveStep Check(in LiveFacts live)
        {
            if (!live.Alive)
            {
                if (live.Sending)
                {
                    return LiveStep.SendGone;
                }

                return LiveStep.Drop;
            }

            if (live.Sending && live.Sent != NoTrap && live.Now != live.Sent)
            {
                return LiveStep.Resend;
            }

            return LiveStep.Keep;
        }

        /// Whether a client applies the host's message now; else it waits
        /// for its world.
        public static bool AppliesNow(bool ready, bool client)
        {
            return ready && client;
        }

        /// What a client does with the host's night object.
        public static ApplyStep Apply(byte kind, bool have, bool prefab)
        {
            if (kind == Gone)
            {
                if (have)
                {
                    return ApplyStep.Destroy;
                }

                return ApplyStep.Forget;
            }

            if (have)
            {
                return ApplyStep.ArmOnly;
            }

            if (!prefab)
            {
                return ApplyStep.NoPrefab;
            }

            if (kind == MadeBefore)
            {
                return ApplyStep.MatchSaved;
            }

            return ApplyStep.Make;
        }

        /// The index of the save's object of this name nearest the
        /// position, closer than ObjRefRules.PlacedSlack (3D, squared,
        /// strict), not tied to an id already; -1 for none. On equal
        /// distance the first wins.
        public static int NearestSaved(
            IReadOnlyList<SavedObject> saved,
            string name,
            Vec3 pos)
        {
            int best = -1;
            float near = ObjRefRules.PlacedSlack * ObjRefRules.PlacedSlack;
            for (int i = 0; i < saved.Count; i++)
            {
                var t = saved[i];
                if (t.Name != name || t.Spawned)
                {
                    continue;
                }

                float d = SquaredDistance(t.Pos, pos);
                if (d < near)
                {
                    near = d;
                    best = i;
                }
            }

            return best;
        }

        /// Whether a client arms an object at all: a state other than
        /// NoTrap (and then only if it has a Trigger).
        public static bool Arms(byte armed)
        {
            return armed != NoTrap;
        }

        /// When an object armed now is armed again.
        public static Seconds RearmAt(Seconds now)
        {
            return new Seconds(now.Value + RearmAfter);
        }

        /// A pending second arming, each frame.
        public static RearmStep Rearm(in RearmFacts rearm)
        {
            if (rearm.Now.Value < rearm.At.Value)
            {
                return RearmStep.Wait;
            }

            if (rearm.HasTrap && rearm.Active != (rearm.Armed == Armed))
            {
                return RearmStep.Set;
            }

            return RearmStep.Drop;
        }

        /// As Unity's (a - b).sqrMagnitude.
        static float SquaredDistance(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }
    }
}
