namespace MonoTorrent.Connections.Utp
{
    sealed class UtpReceivedSegment
    {
        public UtpReceivedSegment (byte[]? buffer, int length, bool isFin)
        {
            Buffer = buffer;
            Length = length;
            IsFin = isFin;
        }

        public byte[]? Buffer { get; }

        public bool IsFin { get; }

        public int Length { get; set; }

        public int Offset { get; set; }
    }
}
