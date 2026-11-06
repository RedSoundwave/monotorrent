using System.Diagnostics;

namespace MonoTorrent.Connections.Utp
{
    static class UtpClock
    {
        public static long NowMicroseconds {
            get {
                long ticks = Stopwatch.GetTimestamp ();
                long frequency = Stopwatch.Frequency;
                return ticks / frequency * 1_000_000 + ticks % frequency * 1_000_000 / frequency;
            }
        }
    }
}
