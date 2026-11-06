using System;
using System.Buffers.Binary;

namespace MonoTorrent.Connections.Utp
{
    public static class UtpPacketCodec
    {
        public const int HeaderLength = 20;
        public const byte Version = 1;
        public const byte SelectiveAckExtension = 1;

        public static bool IsUtpPacket (ReadOnlySpan<byte> datagram)
            => datagram.Length >= HeaderLength
            && (datagram[0] & 0x0F) == Version
            && (datagram[0] >> 4) <= (int) UtpPacketType.Syn;

        public static int GetLength (int selectiveAckLength, int payloadLength)
            => HeaderLength + (selectiveAckLength > 0 ? 2 + selectiveAckLength : 0) + payloadLength;

        public static int Write (Span<byte> destination, in UtpHeader header, ReadOnlySpan<byte> selectiveAck, ReadOnlySpan<byte> payload)
        {
            destination[0] = (byte) (((int) header.Type << 4) | Version);
            destination[1] = selectiveAck.IsEmpty ? (byte) 0 : SelectiveAckExtension;
            BinaryPrimitives.WriteUInt16BigEndian (destination.Slice (2), header.ConnectionId);
            BinaryPrimitives.WriteUInt32BigEndian (destination.Slice (4), header.TimestampMicroseconds);
            BinaryPrimitives.WriteUInt32BigEndian (destination.Slice (8), header.TimestampDifferenceMicroseconds);
            BinaryPrimitives.WriteUInt32BigEndian (destination.Slice (12), header.WindowSize);
            BinaryPrimitives.WriteUInt16BigEndian (destination.Slice (16), header.SequenceNumber);
            BinaryPrimitives.WriteUInt16BigEndian (destination.Slice (18), header.AckNumber);

            int offset = HeaderLength;
            if (!selectiveAck.IsEmpty) {
                destination[offset++] = 0;
                destination[offset++] = (byte) selectiveAck.Length;
                selectiveAck.CopyTo (destination.Slice (offset));
                offset += selectiveAck.Length;
            }
            payload.CopyTo (destination.Slice (offset));
            return offset + payload.Length;
        }

        public static bool TryRead (ReadOnlySpan<byte> datagram, out UtpHeader header, out ReadOnlySpan<byte> selectiveAck, out ReadOnlySpan<byte> payload)
        {
            header = default;
            selectiveAck = default;
            payload = default;
            if (!IsUtpPacket (datagram))
                return false;

            header = new UtpHeader (
                (UtpPacketType) (datagram[0] >> 4),
                BinaryPrimitives.ReadUInt16BigEndian (datagram.Slice (2)),
                BinaryPrimitives.ReadUInt32BigEndian (datagram.Slice (4)),
                BinaryPrimitives.ReadUInt32BigEndian (datagram.Slice (8)),
                BinaryPrimitives.ReadUInt32BigEndian (datagram.Slice (12)),
                BinaryPrimitives.ReadUInt16BigEndian (datagram.Slice (16)),
                BinaryPrimitives.ReadUInt16BigEndian (datagram.Slice (18)));

            int extension = datagram[1];
            int offset = HeaderLength;
            while (extension != 0) {
                if (offset + 2 > datagram.Length)
                    return false;
                int next = datagram[offset];
                int length = datagram[offset + 1];
                offset += 2;
                if (offset + length > datagram.Length)
                    return false;
                if (extension == SelectiveAckExtension)
                    selectiveAck = datagram.Slice (offset, length);
                offset += length;
                extension = next;
            }

            payload = datagram.Slice (offset);
            return true;
        }
    }
}
