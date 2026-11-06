namespace MonoTorrent.Connections.Utp
{
    public static class UtpSequence
    {
        public static bool IsLess (ushort left, ushort right)
            => left != right && (ushort) (right - left) < 0x8000;

        public static bool IsLessOrEqual (ushort left, ushort right)
            => (ushort) (right - left) < 0x8000;

        public static int Distance (ushort from, ushort to)
            => (ushort) (to - from);
    }
}
