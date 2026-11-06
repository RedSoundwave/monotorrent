using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MonoTorrent.Connections.Utp
{
    public sealed class UtpSocketManager : IDisposable
    {
        static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds (50);

        readonly ConcurrentDictionary<UtpConnectionKey, UtpConnection> connections = new ConcurrentDictionary<UtpConnectionKey, UtpConnection> ();
        readonly Func<ReadOnlyMemory<byte>, IPEndPoint, ValueTask> send;
        readonly Timer timer;
        int disposed;
        int ticking;

        public UtpSocketManager (Func<ReadOnlyMemory<byte>, IPEndPoint, ValueTask> send)
        {
            this.send = send ?? throw new ArgumentNullException (nameof (send));
            timer = new Timer (_ => Tick (), null, TickInterval, TickInterval);
        }

        public event Action<UtpConnection>? ConnectionAccepted;

        public bool AcceptIncoming { get; set; } = true;

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds (6);

        public static bool IsUtpPacket (ReadOnlySpan<byte> datagram)
            => UtpPacketCodec.IsUtpPacket (datagram);

        public void HandlePacket (ReadOnlySpan<byte> datagram, IPEndPoint from)
        {
            if (Volatile.Read (ref disposed) != 0 || !UtpPacketCodec.TryRead (datagram, out var header, out var selectiveAck, out var payload))
                return;

            long now = UtpClock.NowMicroseconds;
            if (header.Type == UtpPacketType.Syn) {
                HandleSyn (header, from, now);
            } else if (connections.TryGetValue (new UtpConnectionKey (from, header.ConnectionId), out var connection)) {
                connection.OnPacket (header, selectiveAck, payload, now);
            } else if (header.Type == UtpPacketType.Reset) {
                FindResetTarget (from, header.ConnectionId)?.OnPacket (header, default, default, now);
            } else {
                SendReset (from, header.ConnectionId, header.SequenceNumber, now);
            }
        }

        public async Task<UtpConnection> ConnectAsync (IPEndPoint remote, CancellationToken token)
        {
            if (Volatile.Read (ref disposed) != 0)
                throw new ObjectDisposedException (nameof (UtpSocketManager));
            token.ThrowIfCancellationRequested ();

            UtpConnection connection;
            do {
                ushort receiveId = (ushort) Random.Shared.Next (0x10000);
                connection = new UtpConnection (this, remote, false, receiveId, (ushort) (receiveId + 1), RandomSequenceNumber ());
            } while (!connections.TryAdd (new UtpConnectionKey (remote, connection.ReceiveId), connection));

            var task = connection.Connect (UtpClock.NowMicroseconds, ConnectTimeout);
            using (token.Register (() => connection.Abort (new OperationCanceledException (token))))
                return await task.ConfigureAwait (false);
        }

        public void Dispose ()
        {
            if (Interlocked.Exchange (ref disposed, 1) != 0)
                return;
            timer.Dispose ();
            foreach (var pair in connections)
                pair.Value.Abort (new ObjectDisposedException (nameof (UtpSocketManager)));
            connections.Clear ();
        }

        internal void Remove (UtpConnection connection)
            => connections.TryRemove (new KeyValuePair<UtpConnectionKey, UtpConnection> (new UtpConnectionKey (connection.RemoteEndPoint, connection.ReceiveId), connection));

        internal void Transmit (byte[] buffer, int length, IPEndPoint remote)
        {
            ValueTask task;
            try {
                task = send (buffer.AsMemory (0, length), remote);
            } catch {
                ArrayPool<byte>.Shared.Return (buffer);
                return;
            }

            if (task.IsCompletedSuccessfully) {
                ArrayPool<byte>.Shared.Return (buffer);
            } else {
                CompleteTransmit (task, buffer);
            }
        }

        static async void CompleteTransmit (ValueTask task, byte[] buffer)
        {
            try {
                await task.ConfigureAwait (false);
            } catch {
            } finally {
                ArrayPool<byte>.Shared.Return (buffer);
            }
        }

        void HandleSyn (in UtpHeader syn, IPEndPoint from, long now)
        {
            var key = new UtpConnectionKey (from, (ushort) (syn.ConnectionId + 1));
            if (connections.TryGetValue (key, out var existing)) {
                existing.OnPacket (syn, default, default, now);
                return;
            }
            if (!AcceptIncoming)
                return;

            var connection = new UtpConnection (this, from, true, key.ConnectionId, syn.ConnectionId, RandomSequenceNumber ());
            if (!connections.TryAdd (key, connection))
                return;
            connection.Accept (syn, now);
            ConnectionAccepted?.Invoke (connection);
        }

        UtpConnection? FindResetTarget (IPEndPoint from, ushort connectionId)
        {
            if (connections.TryGetValue (new UtpConnectionKey (from, (ushort) (connectionId + 1)), out var connection) && connection.SendId == connectionId)
                return connection;
            if (connections.TryGetValue (new UtpConnectionKey (from, (ushort) (connectionId - 1)), out connection) && connection.SendId == connectionId)
                return connection;
            return null;
        }

        void SendReset (IPEndPoint remote, ushort connectionId, ushort ackNumber, long now)
        {
            var header = new UtpHeader (UtpPacketType.Reset, connectionId, (uint) now, 0, 0, RandomSequenceNumber (), ackNumber);
            var buffer = ArrayPool<byte>.Shared.Rent (UtpPacketCodec.HeaderLength);
            int length = UtpPacketCodec.Write (buffer, header, default, default);
            Transmit (buffer, length, remote);
        }

        void Tick ()
        {
            if (Volatile.Read (ref disposed) != 0 || Interlocked.Exchange (ref ticking, 1) != 0)
                return;
            try {
                long now = UtpClock.NowMicroseconds;
                foreach (var pair in connections) {
                    try {
                        pair.Value.Tick (now);
                    } catch (Exception ex) {
                        pair.Value.Abort (ex);
                    }
                }
            } finally {
                Volatile.Write (ref ticking, 0);
            }
        }

        static ushort RandomSequenceNumber ()
            => (ushort) Random.Shared.Next (0x10000);
    }
}
