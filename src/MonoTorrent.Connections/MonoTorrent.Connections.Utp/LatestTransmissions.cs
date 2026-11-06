namespace MonoTorrent.Connections.Utp
{
    struct LatestTransmissions
    {
        long first;
        long second;

        public long Third { get; private set; }

        public void Add (long order)
        {
            if (order > first) {
                Third = second;
                second = first;
                first = order;
            } else if (order > second) {
                Third = second;
                second = order;
            } else if (order > Third) {
                Third = order;
            }
        }
    }
}
