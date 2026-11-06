using System;
using System.Buffers;
using System.Collections.Generic;

namespace MonoTorrent.Connections.Utp
{
    public sealed partial class UtpConnection
    {
        const int MaxReorderDistance = 0x4000;

        readonly Dictionary<ushort, UtpReceivedSegment> outOfOrder = new Dictionary<ushort, UtpReceivedSegment> ();
        readonly Queue<UtpReceivedSegment> received = new Queue<UtpReceivedSegment> ();
        int outOfOrderBytes;
        int receivedBytes;
        ushort ackNumber;
        bool ackNeeded;
        bool peerFinished;
        uint lastAdvertisedWindow;

        uint AdvertisedWindow => (uint) Math.Max (0, ReceiveBufferSize - receivedBytes - outOfOrderBytes);

        void ProcessIncoming (in UtpHeader header, ReadOnlySpan<byte> payload, long now)
        {
            ackNeeded = true;
            if (peerFinished)
                return;

            ushort sequence = header.SequenceNumber;
            int distance = UtpSequence.Distance ((ushort) (ackNumber + 1), sequence);
            if (distance >= MaxReorderDistance)
                return;
            if (distance > 0 && outOfOrder.ContainsKey (sequence))
                return;
            if (payload.Length > 0 && receivedBytes + outOfOrderBytes + payload.Length > ReceiveBufferSize)
                return;

            var segment = CreateSegment (payload, header.Type == UtpPacketType.Fin);
            if (distance > 0) {
                outOfOrder.Add (sequence, segment);
                outOfOrderBytes += segment.Length;
                return;
            }

            Deliver (segment, now);
            while (!peerFinished && outOfOrder.Remove ((ushort) (ackNumber + 1), out var next)) {
                outOfOrderBytes -= next.Length;
                Deliver (next, now);
            }
            SignalReceive ();
        }

        static UtpReceivedSegment CreateSegment (ReadOnlySpan<byte> payload, bool isFin)
        {
            if (payload.IsEmpty)
                return new UtpReceivedSegment (null, 0, isFin);
            var buffer = ArrayPool<byte>.Shared.Rent (payload.Length);
            payload.CopyTo (buffer);
            return new UtpReceivedSegment (buffer, payload.Length, isFin);
        }

        void Deliver (UtpReceivedSegment segment, long now)
        {
            ackNumber++;
            if (segment.Length > 0) {
                received.Enqueue (segment);
                receivedBytes += segment.Length;
            }
            if (!segment.IsFin)
                return;

            peerFinished = true;
            ReleaseOutOfOrder ();
            if (finAcked)
                ScheduleRemoval (now);
        }

        int ReadReceived (Span<byte> destination)
        {
            int total = 0;
            while (total < destination.Length && received.Count > 0) {
                var segment = received.Peek ();
                int count = Math.Min (segment.Length, destination.Length - total);
                segment.Buffer.AsSpan (segment.Offset, count).CopyTo (destination.Slice (total));
                segment.Offset += count;
                segment.Length -= count;
                total += count;
                if (segment.Length == 0) {
                    received.Dequeue ();
                    ArrayPool<byte>.Shared.Return (segment.Buffer!);
                }
            }
            receivedBytes -= total;
            return total;
        }

        void SendWindowUpdateIfNeeded (long now)
        {
            if (state != UtpConnectionState.Connected || lastAdvertisedWindow >= ReceiveBufferSize / 2)
                return;
            if (AdvertisedWindow >= lastAdvertisedWindow + ReceiveBufferSize / 4)
                SendState (now);
        }

        void ReleaseOutOfOrder ()
        {
            foreach (var segment in outOfOrder.Values)
                if (segment.Buffer != null)
                    ArrayPool<byte>.Shared.Return (segment.Buffer);
            outOfOrder.Clear ();
            outOfOrderBytes = 0;
        }

        void ReleaseReceiveBuffers ()
        {
            ReleaseOutOfOrder ();
            foreach (var segment in received)
                ArrayPool<byte>.Shared.Return (segment.Buffer!);
            received.Clear ();
            receivedBytes = 0;
        }
    }
}
