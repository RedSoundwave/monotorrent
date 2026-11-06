namespace MonoTorrent.Connections.Utp
{
    public sealed class DelayHistory
    {
        public const long BucketMicroseconds = 60_000_000;

        uint current;
        long currentStartedAt;
        bool hasPrevious;
        uint previous;

        public bool HasSamples { get; private set; }

        public uint BaseDelay => hasPrevious && IsEarlier (previous, current) ? previous : current;

        public void Add (uint sample, long nowMicroseconds)
        {
            if (!HasSamples) {
                current = sample;
                currentStartedAt = nowMicroseconds;
                HasSamples = true;
            } else if (nowMicroseconds - currentStartedAt >= BucketMicroseconds) {
                previous = current;
                hasPrevious = true;
                current = sample;
                currentStartedAt = nowMicroseconds;
            } else if (IsEarlier (sample, current)) {
                current = sample;
            }
        }

        public long GetQueuingDelay (uint sample)
        {
            int delay = (int) (sample - BaseDelay);
            return delay < 0 ? 0 : delay;
        }

        static bool IsEarlier (uint left, uint right)
            => (int) (left - right) < 0;
    }
}
