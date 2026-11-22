using DarkwoodMP.Net;

namespace DarkwoodMP.World.Rolls
{
    /// The bytes of MsgType.Weather (host to client): u8 flags (1 rain
    /// today, 2 lightning before it), f32 rain start, f32 duration, f32
    /// pre-rain lightning time, f32 next lightning, i32 fog in time, i32
    /// fog in day, i32 fog out time, i32 fog out day (-1: none).
    public static class WeatherMessage
    {
        public const byte Rain = 1;
        public const byte Lightning = 2;

        /// The payload, after Begin(MsgType.Weather).
        public static void Write(PacketWriter w, in WeatherFacts rain)
        {
            byte flags = 0;
            if (rain.RainToday)
            {
                flags |= Rain;
            }

            if (rain.Lightning)
            {
                flags |= Lightning;
            }

            w.W.Write(flags);
            w.W.Write(rain.Start);
            w.W.Write(rain.Duration);
            w.W.Write(rain.LightningStart);
            w.W.Write(rain.LightningFor);
            WriteFog(w, rain.FogIn);
            WriteFog(w, rain.FogOut);
        }

        public static WeatherFacts Read(PacketReader r)
        {
            byte flags = r.R.ReadByte();
            float start = r.R.ReadSingle();
            float duration = r.R.ReadSingle();
            float lightningStart = r.R.ReadSingle();
            float lightningFor = r.R.ReadSingle();
            FogTime fogIn = ReadFog(r);
            FogTime fogOut = ReadFog(r);
            return new WeatherFacts
            {
                RainToday = (flags & Rain) != 0,
                Start = start,
                Duration = duration,
                Lightning = (flags & Lightning) != 0,
                LightningStart = lightningStart,
                LightningFor = lightningFor,
                FogIn = fogIn,
                FogOut = fogOut,
            };
        }

        /// i32 time, i32 day; both -1 for none.
        static void WriteFog(PacketWriter w, FogTime fog)
        {
            int time = -1;
            int day = -1;
            if (fog.Set)
            {
                time = fog.Time;
                day = fog.Day;
            }

            w.W.Write(time);
            w.W.Write(day);
        }

        /// A negative time is none.
        static FogTime ReadFog(PacketReader r)
        {
            int time = r.R.ReadInt32();
            int day = r.R.ReadInt32();
            if (time < 0)
            {
                return new FogTime();
            }

            return new FogTime { Set = true, Day = day, Time = time };
        }
    }
}
