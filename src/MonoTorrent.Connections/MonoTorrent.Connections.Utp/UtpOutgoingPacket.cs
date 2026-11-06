using System;

namespace MonoTorrent.Connections.Utp
{
    sealed class UtpOutgoingPacket
    {
        public UtpOutgoingPacket (UtpPacketType type, byte[]? payload)
        {
            Type = type;
            Payload = payload;
        }

        public bool Acked { get; set; }

        public bool FastResent { get; set; }

        public bool InFlight { get; set; }

        public int Length { get; set; }

        public bool NeedsResend { get; set; }

        public byte[]? Payload { get; set; }

        public long SentAt { get; set; }

        public ushort SequenceNumber { get; set; }

        public long TransmitOrder { get; set; }

        public int Transmissions { get; set; }

        public UtpPacketType Type { get; }

        public ReadOnlySpan<byte> Data => Payload is null ? default : Payload.AsSpan (0, Length);
    }
}
