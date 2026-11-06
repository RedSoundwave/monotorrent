using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace MonoTorrent.Connections.Utp
{
    public sealed partial class UtpConnection
    {
        const int MaxOutstandingPackets = 1000;
        const int MaxTimeouts = 7;
        const int DuplicateAckThreshold = 3;

        readonly Dictionary<ushort, UtpOutgoingPacket> outstanding = new Dictionary<ushort, UtpOutgoingPacket> ();
        readonly Queue<UtpOutgoingPacket> unsent = new Queue<UtpOutgoingPacket> ();
        UtpOutgoingPacket? unsentTail;
        int unsentBytes;

        ushort sequenceNumber;
        ushort oldestUnacked;
        ushort lastAckReceived;
        ushort lossSequence;
        int duplicateAcks;
        int bytesInFlight;
        int resendCount;
        int timeouts;
        long retransmitDeadline;
        long transmitCounter;
        uint peerWindow = ReceiveBufferSize;
        bool windowLimited;
        bool finQueued;
        bool finAcked;

        int Enqueue (ReadOnlySpan<byte> data)
        {
            int total = 0;
            while (total < data.Length && unsentBytes < SendBufferSize) {
                if (unsentTail == null || unsentTail.Type != UtpPacketType.Data || unsentTail.Length == MaxPayloadSize) {
                    unsentTail = new UtpOutgoingPacket (UtpPacketType.Data, ArrayPool<byte>.Shared.Rent (MaxPayloadSize));
                    unsent.Enqueue (unsentTail);
                }
                int count = Math.Min (data.Length - total, MaxPayloadSize - unsentTail.Length);
                count = Math.Min (count, SendBufferSize - unsentBytes);
                data.Slice (total, count).CopyTo (unsentTail.Payload.AsSpan (unsentTail.Length));
                unsentTail.Length += count;
                unsentBytes += count;
                total += count;
            }
            return total;
        }

        void EnqueueFin ()
        {
            if (finQueued)
                return;
            finQueued = true;
            unsentTail = new UtpOutgoingPacket (UtpPacketType.Fin, null);
            unsent.Enqueue (unsentTail);
        }

        void Flush (long now)
        {
            if (state != UtpConnectionState.Connected)
                return;

            bool sent = false;
            windowLimited = false;
            if (resendCount > 0) {
                for (ushort sequence = oldestUnacked; sequence != sequenceNumber && resendCount > 0; sequence++) {
                    if (!outstanding.TryGetValue (sequence, out var packet) || !packet.NeedsResend)
                        continue;
                    if (!CanSend (packet.Length)) {
                        windowLimited = true;
                        break;
                    }
                    Transmit (packet, now);
                    sent = true;
                }
            }

            bool released = false;
            while (unsent.Count > 0 && !windowLimited) {
                var packet = unsent.Peek ();
                if (outstanding.Count >= MaxOutstandingPackets || !CanSend (packet.Length)) {
                    windowLimited = true;
                    break;
                }
                unsent.Dequeue ();
                if (packet == unsentTail)
                    unsentTail = null;
                unsentBytes -= packet.Length;
                packet.SequenceNumber = sequenceNumber++;
                outstanding.Add (packet.SequenceNumber, packet);
                Transmit (packet, now);
                sent = true;
                released = true;
            }

            if (released)
                SignalSend ();
            if (ackNeeded && !sent)
                SendState (now);
            ackNeeded = false;
        }

        bool CanSend (int payloadLength)
        {
            if (bytesInFlight == 0)
                return true;
            long window = Math.Min (ledbat.Window, peerWindow);
            return bytesInFlight + payloadLength + UtpPacketCodec.HeaderLength <= window;
        }

        void Transmit (UtpOutgoingPacket packet, long now)
        {
            if (packet.NeedsResend) {
                packet.NeedsResend = false;
                resendCount--;
            }
            packet.Transmissions++;
            packet.TransmitOrder = ++transmitCounter;
            packet.SentAt = now;
            SetInFlight (packet, true);
            Send (packet.Type, packet.SequenceNumber, packet.Data, now);
            if (retransmitDeadline == 0)
                retransmitDeadline = now + rtt.RtoMicroseconds;
        }

        void SetInFlight (UtpOutgoingPacket packet, bool inFlight)
        {
            if (packet.InFlight == inFlight)
                return;
            packet.InFlight = inFlight;
            bytesInFlight += (inFlight ? 1 : -1) * (packet.Length + UtpPacketCodec.HeaderLength);
        }

        void ProcessAck (in UtpHeader header, ReadOnlySpan<byte> selectiveAck, long now)
        {
            ushort ack = header.AckNumber;
            ushort lastSent = (ushort) (sequenceNumber - 1);
            if (!UtpSequence.IsLessOrEqual ((ushort) (oldestUnacked - 1), ack) || !UtpSequence.IsLessOrEqual (ack, lastSent))
                return;

            int bytesAcked = 0;
            bool progress = false;
            while (UtpSequence.IsLessOrEqual (oldestUnacked, ack)) {
                if (outstanding.Remove (oldestUnacked, out var packet)) {
                    if (!packet.Acked)
                        bytesAcked += Acknowledge (packet, now);
                    if (packet.Type == UtpPacketType.Fin) {
                        finAcked = true;
                        ScheduleRemoval (now);
                    }
                }
                oldestUnacked++;
                progress = true;
            }

            if (!selectiveAck.IsEmpty)
                bytesAcked += ProcessSelectiveAck (ack, selectiveAck, now, ref progress);

            if (ack != lastAckReceived) {
                lastAckReceived = ack;
                duplicateAcks = 0;
            } else if (header.Type == UtpPacketType.State && outstanding.Count > 0 && ++duplicateAcks >= DuplicateAckThreshold) {
                if (outstanding.TryGetValue ((ushort) (ack + 1), out var first) && !first.Acked && !first.FastResent)
                    MarkLost (first);
            }

            if (progress) {
                timeouts = 0;
                retransmitDeadline = outstanding.Count > 0 ? now + rtt.RtoMicroseconds : 0;
            }

            if (bytesAcked > 0) {
                long queuingDelay = 0;
                if (header.TimestampDifferenceMicroseconds != 0) {
                    delays.Add (header.TimestampDifferenceMicroseconds, now);
                    queuingDelay = delays.GetQueuingDelay (header.TimestampDifferenceMicroseconds);
                }
                ledbat.OnAck (bytesAcked, queuingDelay, windowLimited);
            }
        }

        int ProcessSelectiveAck (ushort ack, ReadOnlySpan<byte> selectiveAck, long now, ref bool progress)
        {
            int bytesAcked = 0;
            var newestAcked = new LatestTransmissions ();
            for (int bit = selectiveAck.Length * 8 - 1; bit >= -1; bit--) {
                ushort sequence = SelectiveAck.GetSequenceNumber (ack, bit);
                if (!outstanding.TryGetValue (sequence, out var packet))
                    continue;
                if (bit >= 0 && SelectiveAck.IsSet (selectiveAck, bit)) {
                    if (!packet.Acked) {
                        bytesAcked += Acknowledge (packet, now);
                        progress = true;
                    }
                    newestAcked.Add (packet.TransmitOrder);
                } else if (!packet.Acked && !packet.NeedsResend && newestAcked.Third > packet.TransmitOrder) {
                    MarkLost (packet);
                }
            }
            return bytesAcked;
        }

        int Acknowledge (UtpOutgoingPacket packet, long now)
        {
            SetInFlight (packet, false);
            if (packet.NeedsResend) {
                packet.NeedsResend = false;
                resendCount--;
            }
            if (packet.Transmissions == 1)
                rtt.AddSample (now - packet.SentAt);
            packet.Acked = true;
            ReturnPayload (packet);
            return packet.Length + UtpPacketCodec.HeaderLength;
        }

        void MarkLost (UtpOutgoingPacket packet)
        {
            SetInFlight (packet, false);
            packet.FastResent = true;
            if (!packet.NeedsResend) {
                packet.NeedsResend = true;
                resendCount++;
            }
            if (UtpSequence.IsLess (lossSequence, packet.SequenceNumber)) {
                ledbat.OnLoss ();
                lossSequence = (ushort) (sequenceNumber - 1);
            }
        }

        void OnRetransmitTimeout (long now)
        {
            bool pending = false;
            foreach (var packet in outstanding.Values) {
                if (packet.Acked)
                    continue;
                pending = true;
                SetInFlight (packet, false);
                packet.FastResent = false;
                if (!packet.NeedsResend) {
                    packet.NeedsResend = true;
                    resendCount++;
                }
            }

            if (!pending) {
                retransmitDeadline = 0;
                return;
            }
            if (IsPeerWindowClosed && !disposed) {
                retransmitDeadline = now + rtt.RtoMicroseconds;
                return;
            }
            if (++timeouts > MaxTimeouts) {
                Fail (new IOException ($"The uTP connection to {RemoteEndPoint} exceeded the retransmission limit"));
                return;
            }
            rtt.Backoff ();
            ledbat.OnTimeout ();
            lossSequence = (ushort) (sequenceNumber - 1);
            retransmitDeadline = now + rtt.RtoMicroseconds;
        }

        bool IsPeerWindowClosed => peerWindow < MaxPayloadSize + UtpPacketCodec.HeaderLength;

        void UpdatePeerWindow (uint window, long now)
        {
            bool wasClosed = IsPeerWindowClosed;
            peerWindow = window;
            if (!wasClosed || IsPeerWindowClosed)
                return;

            foreach (var packet in outstanding.Values) {
                if (packet.Acked || !packet.InFlight)
                    continue;
                SetInFlight (packet, false);
                packet.NeedsResend = true;
                resendCount++;
            }
            retransmitDeadline = now + rtt.RtoMicroseconds;
        }

        static void ReturnPayload (UtpOutgoingPacket packet)
        {
            if (packet.Payload != null)
                ArrayPool<byte>.Shared.Return (packet.Payload);
            packet.Payload = null;
        }

        void ReleaseSendBuffers ()
        {
            foreach (var packet in outstanding.Values)
                ReturnPayload (packet);
            foreach (var packet in unsent)
                ReturnPayload (packet);
            outstanding.Clear ();
            unsent.Clear ();
            unsentTail = null;
            unsentBytes = 0;
            bytesInFlight = 0;
            resendCount = 0;
            retransmitDeadline = 0;
        }
    }
}
