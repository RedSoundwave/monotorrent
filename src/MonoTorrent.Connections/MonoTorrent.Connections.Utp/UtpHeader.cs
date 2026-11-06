namespace MonoTorrent.Connections.Utp
{
    public readonly record struct UtpHeader (
        UtpPacketType Type,
        ushort ConnectionId,
        uint TimestampMicroseconds,
        uint TimestampDifferenceMicroseconds,
        uint WindowSize,
        ushort SequenceNumber,
        ushort AckNumber);
}
