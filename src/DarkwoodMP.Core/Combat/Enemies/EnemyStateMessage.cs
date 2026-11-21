using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemyState (host -> client): f32 the host's
    /// clock, u16 n, n x (u32 id, vec3 position, quat rotation, u8 flags,
    /// f32 health, u8 m, m x (i32 animator, i32 clip, u16 frame, u8
    /// playing)). The host writes the head, then each enemy and its
    /// animators, then the count.
    public static class EnemyStateMessage
    {
        /// Alive, or in a pre-death stage.
        public const byte Alive = 1;
        public const byte Asleep = 2;
        /// Being cut in half (Character.cuttingInHalf).
        public const byte Cut = 4;
        /// Animators sent for one enemy at most.
        public const int MostAnims = 8;

        /// The flags of one enemy.
        public static byte FlagsOf(bool alive, bool asleep, bool cut)
        {
            byte flags = 0;
            if (alive)
            {
                flags |= Alive;
            }

            if (asleep)
            {
                flags |= Asleep;
            }

            if (cut)
            {
                flags |= Cut;
            }

            return flags;
        }

        /// The head, after Begin(MsgType.EnemyState), with a count of 0.
        /// Returns where the count is, for SetCount.
        public static int WriteHead(PacketWriter w, float sent)
        {
            w.W.Write(sent);
            int at = w.Length;
            w.W.Write((ushort)0);
            return at;
        }

        /// One enemy, without its animators: anims of them follow.
        public static void WriteFrame(
            PacketWriter w, in EnemyFrame e, int anims)
        {
            w.W.Write(e.Id);
            w.Write(e.At);
            QuatBytes.Write(w, e.Rot);
            w.W.Write(e.Flags);
            w.W.Write(e.Health);
            w.W.Write((byte)anims);
        }

        public static void WriteAnim(PacketWriter w, in EnemyAnim a)
        {
            w.W.Write(a.Animator);
            w.W.Write(a.Clip);
            w.W.Write(a.Frame);
            w.W.Write(Flag.Of(a.Playing));
        }

        /// The count of enemies written, at WriteHead's place.
        public static void SetCount(PacketWriter w, int at, int count)
        {
            var buf = w.Buffer;
            buf[at] = (byte)count;
            buf[at + 1] = (byte)(count >> 8);
        }

        public static EnemyStates Read(PacketReader r)
        {
            float sent = r.R.ReadSingle();
            int n = r.R.ReadUInt16();
            var frames = new List<EnemyFrame>(n);
            for (int i = 0; i < n; i++)
            {
                frames.Add(ReadFrame(r));
            }

            return new EnemyStates
            {
                Sent = sent,
                Frames = frames,
            };
        }

        private static EnemyFrame ReadFrame(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            Vec3 at = r.ReadVec3();
            Quat rot = QuatBytes.Read(r);
            byte flags = r.R.ReadByte();
            float health = r.R.ReadSingle();
            int m = r.R.ReadByte();
            var anims = new EnemyAnim[m];
            for (int k = 0; k < m; k++)
            {
                anims[k] = ReadAnim(r);
            }

            return new EnemyFrame
            {
                Id = id,
                At = at,
                Rot = rot,
                Flags = flags,
                Health = health,
                Anims = anims,
            };
        }

        private static EnemyAnim ReadAnim(PacketReader r)
        {
            int animator = r.R.ReadInt32();
            int clip = r.R.ReadInt32();
            ushort frame = r.R.ReadUInt16();
            bool playing = r.R.ReadByte() != 0;
            return new EnemyAnim
            {
                Animator = animator,
                Clip = clip,
                Frame = frame,
                Playing = playing,
            };
        }
    }
}
