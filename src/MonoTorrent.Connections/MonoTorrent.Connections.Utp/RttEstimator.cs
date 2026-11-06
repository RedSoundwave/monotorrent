using System;

namespace MonoTorrent.Connections.Utp
{
    public sealed class RttEstimator
    {
        public const long MinRtoMicroseconds = 500_000;
        public const long InitialRtoMicroseconds = 1_000_000;
        public const long MaxRtoMicroseconds = 60_000_000;

        public bool HasSample { get; private set; }

        public long RtoMicroseconds { get; private set; } = InitialRtoMicroseconds;

        public long SmoothedRttMicroseconds { get; private set; }

        public long RttVarianceMicroseconds { get; private set; }

        public void AddSample (long rttMicroseconds)
        {
            if (rttMicroseconds < 0)
                return;

            if (HasSample) {
                RttVarianceMicroseconds = (3 * RttVarianceMicroseconds + Math.Abs (SmoothedRttMicroseconds - rttMicroseconds)) / 4;
                SmoothedRttMicroseconds = (7 * SmoothedRttMicroseconds + rttMicroseconds) / 8;
            } else {
                SmoothedRttMicroseconds = rttMicroseconds;
                RttVarianceMicroseconds = rttMicroseconds / 2;
                HasSample = true;
            }
            RtoMicroseconds = Math.Clamp (SmoothedRttMicroseconds + 4 * RttVarianceMicroseconds, MinRtoMicroseconds, MaxRtoMicroseconds);
        }

        public void Backoff ()
            => RtoMicroseconds = Math.Min (RtoMicroseconds * 2, MaxRtoMicroseconds);
    }
}
