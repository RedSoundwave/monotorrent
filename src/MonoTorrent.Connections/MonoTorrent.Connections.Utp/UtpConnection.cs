using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MonoTorrent.Connections.Utp
{
    public sealed partial class UtpConnection : IDisposable
    {
        internal const int MaxPayloadSize = 1400;
        internal const int ReceiveBufferSize = 1024 * 1024;
        internal const int SendBufferSize = 1024 * 1024;
        internal const long KeepAliveMicroseconds = 29_000_000;
        internal const long IdleTimeoutMicroseconds = 60_000_000;
        const long TimeWaitMicroseconds = 2_000_000;
        const long CloseLingerMicroseconds = 10_000_000;

        readonly object sync = new object ();
        readonly UtpSocketManager manager;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim (1, 1);
        readonly RttEstimator rtt = new RttEstimator ();
        readonly DelayHistory delays = new DelayHistory ();
        readonly LedbatController ledbat = new LedbatController (MaxPayloadSize + UtpPacketCodec.HeaderLength, ReceiveBufferSize, 4 * (MaxPayloadSize + UtpPacketCodec.HeaderLength));

        List<(byte[] Buffer, int Length)> outbox = new List<(byte[], int)> ();
        UtpConnectionState state = UtpConnectionState.SynSent;
        TaskCompletionSource<UtpConnection>? connectCompletion;
        TaskCompletionSource? receiveWaiter;
        TaskCompletionSource? sendWaiter;
        Exception? error;
        bool disposed;
        long removeAt;
        long lastReceiveAt;
        long lastSendAt;
        uint replyMicroseconds;

        long connectDeadline;
        long synResendAt;
        long synInterval;
        long synSentAt;
        int synTransmissions;
        ushort synSequence;

        internal UtpConnection (UtpSocketManager manager, IPEndPoint remoteEndPoint, bool isIncoming, ushort receiveId, ushort sendId, ushort initialSequenceNumber)
        {
            this.manager = manager;
            RemoteEndPoint = remoteEndPoint;
            IsIncoming = isIncoming;
            ReceiveId = receiveId;
            SendId = sendId;
            sequenceNumber = initialSequenceNumber;
            oldestUnacked = initialSequenceNumber;
            lastAckReceived = (ushort) (initialSequenceNumber - 1);
            lossSequence = (ushort) (initialSequenceNumber - 1);
            lastReceiveAt = lastSendAt = UtpClock.NowMicroseconds;
        }

        public IPEndPoint RemoteEndPoint { get; }

        public bool IsIncoming { get; }

        internal ushort ReceiveId { get; }

        internal ushort SendId { get; }

        internal Task<UtpConnection> Connect (long now, TimeSpan timeout)
        {
            Task<UtpConnection> task;
            lock (sync) {
                connectCompletion = new TaskCompletionSource<UtpConnection> (TaskCreationOptions.RunContinuationsAsynchronously);
                task = connectCompletion.Task;
                synSequence = sequenceNumber++;
                oldestUnacked = sequenceNumber;
                lastAckReceived = synSequence;
                lossSequence = synSequence;
                connectDeadline = now + (long) timeout.TotalMicroseconds;
                synInterval = rtt.RtoMicroseconds;
                synResendAt = now + synInterval;
                TransmitSyn (now);
            }
            DrainOutbox ();
            return task;
        }

        internal void Accept (in UtpHeader syn, long now)
        {
            lock (sync) {
                synSequence = syn.SequenceNumber;
                ackNumber = syn.SequenceNumber;
                peerWindow = syn.WindowSize;
                replyMicroseconds = (uint) now - syn.TimestampMicroseconds;
                lastReceiveAt = now;
                state = UtpConnectionState.Connected;
                SendState (now);
            }
            DrainOutbox ();
        }

        internal void OnPacket (in UtpHeader header, ReadOnlySpan<byte> selectiveAck, ReadOnlySpan<byte> payload, long now)
        {
            lock (sync) {
                if (state == UtpConnectionState.Closed || state == UtpConnectionState.Failed)
                    return;

                lastReceiveAt = now;
                replyMicroseconds = (uint) now - header.TimestampMicroseconds;
                switch (header.Type) {
                    case UtpPacketType.Reset:
                        Fail (new IOException ("The uTP connection was reset by the remote peer"));
                        break;
                    case UtpPacketType.Syn:
                        if (IsIncoming && header.SequenceNumber == synSequence)
                            SendState (now);
                        break;
                    default:
                        if (state == UtpConnectionState.SynSent && !CompleteConnect (header, now))
                            break;
                        ProcessAck (header, selectiveAck, now);
                        UpdatePeerWindow (header.WindowSize, now);
                        if (header.Type == UtpPacketType.Data || header.Type == UtpPacketType.Fin)
                            ProcessIncoming (header, payload, now);
                        Flush (now);
                        break;
                }
            }
            DrainOutbox ();
        }

        internal void Tick (long now)
        {
            lock (sync) {
                if (state == UtpConnectionState.SynSent)
                    TickConnecting (now);
                else if (state == UtpConnectionState.Connected)
                    TickConnected (now);
            }
            DrainOutbox ();
        }

        internal void Abort (Exception reason)
        {
            lock (sync) {
                if (state == UtpConnectionState.Closed || state == UtpConnectionState.Failed)
                    return;
                Send (UtpPacketType.Reset, sequenceNumber, default, UtpClock.NowMicroseconds);
                Fail (reason);
            }
            DrainOutbox ();
        }

        public async ValueTask<int> ReceiveAsync (Memory<byte> buffer, CancellationToken token = default)
        {
            while (true) {
                var waiter = TryReceive (buffer, out int read);
                DrainOutbox ();
                if (waiter == null)
                    return read;
                await waiter.WaitAsync (token).ConfigureAwait (false);
            }
        }

        public async ValueTask SendAsync (ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            await sendLock.WaitAsync (token).ConfigureAwait (false);
            try {
                while (true) {
                    var waiter = TryEnqueue (buffer, out int accepted);
                    DrainOutbox ();
                    buffer = buffer.Slice (accepted);
                    if (waiter == null)
                        return;
                    await waiter.WaitAsync (token).ConfigureAwait (false);
                }
            } finally {
                sendLock.Release ();
            }
        }

        public void Dispose ()
        {
            lock (sync) {
                if (disposed)
                    return;
                disposed = true;
                if (state == UtpConnectionState.Connected) {
                    EnqueueFin ();
                    Flush (UtpClock.NowMicroseconds);
                } else if (state == UtpConnectionState.SynSent) {
                    Send (UtpPacketType.Reset, sequenceNumber, default, UtpClock.NowMicroseconds);
                    Fail (new ObjectDisposedException (nameof (UtpConnection)));
                }
                SignalReceive ();
                SignalSend ();
            }
            DrainOutbox ();
        }

        Task? TryReceive (Memory<byte> buffer, out int read)
        {
            read = 0;
            lock (sync) {
                if (disposed || buffer.IsEmpty)
                    return null;
                if (receivedBytes > 0) {
                    read = ReadReceived (buffer.Span);
                    SendWindowUpdateIfNeeded (UtpClock.NowMicroseconds);
                    return null;
                }
                if (error != null)
                    throw new IOException ("The uTP connection failed", error);
                if (peerFinished || state == UtpConnectionState.Closed)
                    return null;
                return (receiveWaiter ??= new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        Task? TryEnqueue (ReadOnlyMemory<byte> buffer, out int accepted)
        {
            accepted = 0;
            lock (sync) {
                if (disposed)
                    throw new ObjectDisposedException (nameof (UtpConnection));
                if (error != null)
                    throw new IOException ("The uTP connection failed", error);
                if (state != UtpConnectionState.Connected)
                    throw new IOException ("The uTP connection is closed");

                long now = UtpClock.NowMicroseconds;
                while (accepted < buffer.Length) {
                    int count = Enqueue (buffer.Span.Slice (accepted));
                    if (count == 0)
                        break;
                    accepted += count;
                    Flush (now);
                }

                if (accepted == buffer.Length)
                    return null;
                return (sendWaiter ??= new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        bool CompleteConnect (in UtpHeader header, long now)
        {
            if (header.AckNumber != synSequence)
                return false;

            ackNumber = (ushort) (header.SequenceNumber - 1);
            state = UtpConnectionState.Connected;
            if (synTransmissions == 1)
                rtt.AddSample (now - synSentAt);
            connectCompletion?.TrySetResult (this);
            return true;
        }

        void TickConnecting (long now)
        {
            if (now >= connectDeadline) {
                Fail (new TimeoutException ($"The uTP connection to {RemoteEndPoint} timed out"));
            } else if (now >= synResendAt) {
                synInterval = Math.Min (synInterval * 2, RttEstimator.MaxRtoMicroseconds);
                synResendAt = now + synInterval;
                TransmitSyn (now);
            }
        }

        void TickConnected (long now)
        {
            if (removeAt != 0 && now >= removeAt) {
                Close ();
                return;
            }
            if (now - lastReceiveAt >= IdleTimeoutMicroseconds) {
                Fail (new IOException ($"The uTP connection to {RemoteEndPoint} timed out"));
                return;
            }
            if (retransmitDeadline != 0 && now >= retransmitDeadline)
                OnRetransmitTimeout (now);
            if (state != UtpConnectionState.Connected)
                return;
            Flush (now);
            if (now - lastSendAt >= KeepAliveMicroseconds)
                SendState (now);
        }

        void TransmitSyn (long now)
        {
            synTransmissions++;
            synSentAt = now;
            Send (UtpPacketType.Syn, synSequence, default, now);
        }

        void Send (UtpPacketType type, ushort sequence, ReadOnlySpan<byte> payload, long now)
        {
            Span<byte> selectiveAck = stackalloc byte[SelectiveAck.MaxLength];
            int selectiveAckLength = outOfOrder.Count > 0 ? SelectiveAck.Build (ackNumber, outOfOrder, selectiveAck) : 0;
            uint window = AdvertisedWindow;
            ushort connectionId = type == UtpPacketType.Syn ? ReceiveId : SendId;
            var header = new UtpHeader (type, connectionId, (uint) now, replyMicroseconds, window, sequence, ackNumber);

            var buffer = ArrayPool<byte>.Shared.Rent (UtpPacketCodec.GetLength (selectiveAckLength, payload.Length));
            int length = UtpPacketCodec.Write (buffer, header, selectiveAck.Slice (0, selectiveAckLength), payload);
            outbox.Add ((buffer, length));
            lastAdvertisedWindow = window;
            lastSendAt = now;
        }

        void SendState (long now)
        {
            ackNeeded = false;
            Send (UtpPacketType.State, sequenceNumber, default, now);
        }

        void DrainOutbox ()
        {
            List<(byte[] Buffer, int Length)> datagrams;
            lock (sync) {
                if (outbox.Count == 0)
                    return;
                datagrams = outbox;
                outbox = new List<(byte[], int)> ();
            }
            foreach (var datagram in datagrams)
                manager.Transmit (datagram.Buffer, datagram.Length, RemoteEndPoint);
        }

        void ScheduleRemoval (long now)
            => removeAt = now + (peerFinished ? TimeWaitMicroseconds : CloseLingerMicroseconds);

        void Close ()
        {
            state = UtpConnectionState.Closed;
            ReleaseResources ();
        }

        void Fail (Exception reason)
        {
            if (state == UtpConnectionState.Closed || state == UtpConnectionState.Failed)
                return;
            state = UtpConnectionState.Failed;
            error = reason;
            connectCompletion?.TrySetException (reason);
            ReleaseResources ();
        }

        void ReleaseResources ()
        {
            ReleaseSendBuffers ();
            ReleaseReceiveBuffers ();
            SignalReceive ();
            SignalSend ();
            manager.Remove (this);
        }

        void SignalReceive ()
        {
            var waiter = receiveWaiter;
            receiveWaiter = null;
            waiter?.TrySetResult ();
        }

        void SignalSend ()
        {
            var waiter = sendWaiter;
            sendWaiter = null;
            waiter?.TrySetResult ();
        }
    }
}
