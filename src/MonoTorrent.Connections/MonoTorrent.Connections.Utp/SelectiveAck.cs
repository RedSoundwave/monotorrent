using System;
using System.Collections.Generic;

namespace MonoTorrent.Connections.Utp
{
    public static class SelectiveAck
    {
        public const int MaxLength = 32;

        public static int Build<T> (ushort ackNumber, Dictionary<ushort, T> received, Span<byte> destination)
        {
            int length = Math.Min (destination.Length, MaxLength) & ~3;
            destination.Slice (0, length).Clear ();

            ushort first = (ushort) (ackNumber + 2);
            int highest = -1;
            foreach (var sequenceNumber in received.Keys) {
                int bit = UtpSequence.Distance (first, sequenceNumber);
                if (bit >= length * 8)
                    continue;
                destination[bit >> 3] |= (byte) (1 << (bit & 7));
                highest = Math.Max (highest, bit);
            }
            return highest < 0 ? 0 : ((highest >> 5) + 1) * 4;
        }

        public static bool IsSet (ReadOnlySpan<byte> mask, int bit)
            => (mask[bit >> 3] & (1 << (bit & 7))) != 0;

        public static ushort GetSequenceNumber (ushort ackNumber, int bit)
            => (ushort) (ackNumber + 2 + bit);
    }
}
