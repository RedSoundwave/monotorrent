using System;

namespace MonoTorrent.Connections.Utp
{
    public sealed class LedbatController
    {
        public const long TargetDelayMicroseconds = 100_000;
        public const int MaxWindowIncreasePerRtt = 3000;

        double window;
        double slowStartThreshold = double.MaxValue;

        public LedbatController (int minimumWindow, int maximumWindow, int initialWindow)
        {
            MinimumWindow = minimumWindow;
            MaximumWindow = maximumWindow;
            window = Math.Clamp (initialWindow, minimumWindow, maximumWindow);
        }

        public bool InSlowStart { get; private set; } = true;

        public int MaximumWindow { get; }

        public int MinimumWindow { get; }

        public int Window => (int) window;

        public void OnAck (int bytesAcked, long queuingDelayMicroseconds, bool windowLimited)
        {
            if (bytesAcked <= 0)
                return;

            double offTarget = Math.Clamp ((TargetDelayMicroseconds - queuingDelayMicroseconds) / (double) TargetDelayMicroseconds, -1.0, 1.0);
            double windowFactor = Math.Min (bytesAcked, window) / window;
            double gain = MaxWindowIncreasePerRtt * offTarget * windowFactor;

            if (InSlowStart) {
                if (offTarget <= 0 || window >= slowStartThreshold)
                    InSlowStart = false;
                else
                    gain = Math.Max (gain, bytesAcked);
            }

            if (gain > 0 && !windowLimited)
                gain = 0;

            window = Math.Clamp (window + gain, MinimumWindow, MaximumWindow);
        }

        public void OnLoss ()
        {
            window = Math.Max (window / 2, MinimumWindow);
            slowStartThreshold = window;
            InSlowStart = false;
        }

        public void OnTimeout ()
            => OnLoss ();
    }
}
