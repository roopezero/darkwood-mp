using DarkwoodMP.Net;

namespace DarkwoodMP.World.Skills
{
    /// The bytes of MsgType.SkillSpend (client to host, a request): u8
    /// the level, str the good skill, str the negative skill ("" for
    /// none), u8 n, then n times str a tier skill had before.
    public static class SkillSpend
    {
        /// The most skills had that one message lists.
        public const int MostHad = 255;

        /// The payload, after Begin(MsgType.SkillSpend). The level is
        /// clamped to 0 to 255; past MostHad skills had are left out.
        public static void Write(PacketWriter w, in SpendRequest spend)
        {
            int level = spend.Level;
            if (level < 0)
            {
                level = 0;
            }

            if (level > 255)
            {
                level = 255;
            }

            w.W.Write((byte)level);
            w.WriteStr(spend.Good);
            w.WriteStr(spend.Negative);
            int n = 0;
            if (spend.Had != null)
            {
                n = spend.Had.Count;
            }

            if (n > MostHad)
            {
                n = MostHad;
            }

            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                w.WriteStr(spend.Had[i]);
            }
        }

        /// The request; Negative is "" for none.
        public static SpendRequest Read(PacketReader r)
        {
            int level = r.R.ReadByte();
            string good = r.ReadStr();
            string negative = r.ReadStr();
            int n = r.R.ReadByte();
            var had = new string[n];
            for (int i = 0; i < n; i++)
            {
                had[i] = r.ReadStr();
            }

            return new SpendRequest
            {
                Level = level,
                Good = good,
                Negative = negative,
                Had = had,
            };
        }
    }
}
