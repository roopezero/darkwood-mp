namespace DarkwoodMP.World.Npcs
{
    /// Where the NPCs stand (the plugin's NpcSync). The host sends where
    /// its NPCs with a shared save id are, those that moved twice a second
    /// and all of them every 4 seconds; clients move their copies there.
    public static class NpcPlaceRules
    {
        /// An NPC is sent when it moved further than this from where it was
        /// last sent (compared squared).
        public const float Moved = 30f;

        /// Seconds between two ticks, which send the NPCs that moved.
        public const float MovedEvery = 0.5f;

        /// Seconds between two sends of all of them.
        public const float AllEvery = 4f;

        /// The most NPCs one NpcPos message holds.
        public const int PerMessage = 200;

        /// A client's copy this far off is put there at once.
        public const float Snap = 600f;

        /// A client's copy this close is left alone.
        public const float Close = 15f;

        /// The slowest a copy slides, a second.
        public const float SlideSpeed = 250f;

        /// Otherwise it slides this many times its distance, a second.
        public const float SlidePerDistance = 4f;

        /// Whether a save id is one every game shares (the plugin's
        /// SharedIds.SendLimit is sendLimit).
        public static bool Shared(int id, int sendLimit)
        {
            return id > 0 && id < sendLimit;
        }

        /// Whether an NPC's position is shared and set: it is alive, and
        /// NetEnemies doesn't run it.
        public static bool Movable(in NpcFacts npc)
        {
            return npc.HasCharacter && npc.Alive && !npc.RunByEnemies;
        }

        /// Host, each frame in a world: whether this frame ticks, whether
        /// it sends all of them, and the timers to keep.
        public static NpcTick Tick(int peers, Seconds now, NpcTimers timers)
        {
            if (peers == 0 || now < timers.NextMoved)
            {
                return new NpcTick { Timers = timers };
            }

            var next = new Seconds(now.Value + MovedEvery);
            bool all = now.Value >= timers.NextAll.Value;
            var nextAll = timers.NextAll;
            if (all)
            {
                nextAll = new Seconds(now.Value + AllEvery);
            }

            return new NpcTick
            {
                Sends = true,
                All = all,
                Timers = new NpcTimers { NextMoved = next, NextAll = nextAll },
            };
        }

        /// Host, at a tick: whether an NPC is sent. Then where it is
        /// becomes its last sent position.
        public static bool Sends(in NpcSendFacts npc)
        {
            if (!npc.Sent)
            {
                return true;
            }

            float x = npc.At.X - npc.Was.X;
            float z = npc.At.Z - npc.Was.Z;
            bool moved = x * x + z * z > Moved * Moved;
            return moved || npc.All;
        }

        /// How many of count NPCs, from start on, the next message holds.
        public static int BatchSize(int count, int start)
        {
            int left = count - start;
            if (PerMessage < left)
            {
                return PerMessage;
            }

            return left;
        }

        /// Whether an NpcPos message is taken: from the host, in a world.
        public static bool TakesPos(bool fromHost, bool inWorld)
        {
            return fromHost && inWorld;
        }

        /// Client, each frame: what to do with a copy the host placed.
        public static NpcStep Follow(in NpcFollowFacts npc)
        {
            if (!npc.HasObject)
            {
                return new NpcStep { Move = NpcMove.Skip };
            }

            if (!npc.Movable)
            {
                return new NpcStep { Move = NpcMove.Forget };
            }

            var at = npc.At;
            var to = new Vec3(npc.Target.X, at.Y, npc.Target.Z);
            float x = to.X - at.X;
            float z = to.Z - at.Z;
            float d = Sqrt(x * x + z * z);
            if (d <= Close)
            {
                return new NpcStep { Move = NpcMove.Leave, Distance = d };
            }

            if (d > Snap)
            {
                return new NpcStep
                {
                    Move = NpcMove.Snap,
                    To = to,
                    Distance = d,
                };
            }

            float step = Max(SlideSpeed, d * SlidePerDistance)
                * npc.Delta.Value;
            return new NpcStep
            {
                Move = NpcMove.Step,
                To = MoveTowards(at, to, step),
                Distance = d,
            };
        }

        /// As Unity's Vector3.MoveTowards: to, if it is within step of at,
        /// else step from at toward it.
        static Vec3 MoveTowards(Vec3 at, Vec3 to, float step)
        {
            float x = to.X - at.X;
            float y = to.Y - at.Y;
            float z = to.Z - at.Z;
            float sq = x * x + y * y + z * z;
            if (sq == 0f || (step >= 0f && sq <= step * step))
            {
                return to;
            }

            float d = Sqrt(sq);
            return new Vec3(
                at.X + x / d * step,
                at.Y + y / d * step,
                at.Z + z / d * step);
        }

        /// As Mathf.Sqrt.
        static float Sqrt(float value)
        {
            return (float)System.Math.Sqrt(value);
        }

        /// As Mathf.Max: a when it is bigger, else b.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
