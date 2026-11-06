using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace MonoTorrent.Connections.Utp
{
    [TestFixture]
    public class UtpConnectionTests
    {
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds (25);

        [Test]
        public async Task LossyLinkBidirectionalTransfer ()
        {
            using var link = new LossyUtpLink (drop: 0.05, duplicate: 0.02, reorder: 0.05);
            var (client, server) = await ConnectPair (link);

            Assert.IsFalse (client.IsIncoming);
            Assert.IsTrue (server.IsIncoming);
            Assert.AreEqual (LossyUtpLink.EndPointA, server.RemoteEndPoint);
            Assert.AreEqual (LossyUtpLink.EndPointB, client.RemoteEndPoint);

            var clientData = CreateData (4 * 1024 * 1024, 1);
            var serverData = CreateData (4 * 1024 * 1024, 2);

            var clientSend = SendAll (client, clientData);
            var serverSend = SendAll (server, serverData);
            var clientReceive = ReceiveExactly (client, serverData.Length);
            var serverReceive = ReceiveExactly (server, clientData.Length);
            await Task.WhenAll (clientSend, serverSend, clientReceive, serverReceive).WithTimeout (TestTimeout);

            AssertSameBytes (serverData, await clientReceive);
            AssertSameBytes (clientData, await serverReceive);

            client.Dispose ();
            Assert.AreEqual (0, await server.ReceiveAsync (new byte[16]).AsTask ().WithTimeout (TestTimeout));
            server.Dispose ();
            Assert.AreEqual (0, await client.ReceiveAsync (new byte[16]).AsTask ().WithTimeout (TestTimeout));

            await WaitUntil (() => link.FinsFromA > 0 && link.FinsFromB > 0);
        }

        [Test]
        public async Task GracefulCloseDeliversQueuedData ()
        {
            using var link = new LossyUtpLink (drop: 0.02, duplicate: 0, reorder: 0.02);
            var (client, server) = await ConnectPair (link);
            var data = CreateData (300 * 1024, 3);

            await client.SendAsync (data).AsTask ().WithTimeout (TestTimeout);
            client.Dispose ();

            var received = await ReceiveExactly (server, data.Length).WithTimeout (TestTimeout);
            AssertSameBytes (data, received);
            Assert.AreEqual (0, await server.ReceiveAsync (new byte[16]).AsTask ().WithTimeout (TestTimeout));
            server.Dispose ();
        }

        [Test]
        public void ConnectTimesOutWhenEverythingIsDropped ()
        {
            using var link = new LossyUtpLink (drop: 1, duplicate: 0, reorder: 0);
            link.A.ConnectTimeout = TimeSpan.FromSeconds (1.5);

            var stopwatch = Stopwatch.StartNew ();
            Assert.ThrowsAsync<TimeoutException> (() => link.A.ConnectAsync (LossyUtpLink.EndPointB, CancellationToken.None));
            Assert.That (stopwatch.Elapsed, Is.GreaterThanOrEqualTo (TimeSpan.FromSeconds (1.4)));
            Assert.That (stopwatch.Elapsed, Is.LessThan (TimeSpan.FromSeconds (5)));
            Assert.That (link.DatagramsFromA, Is.GreaterThanOrEqualTo (2));
        }

        [Test]
        public void ConnectCanBeCancelled ()
        {
            using var link = new LossyUtpLink (drop: 1, duplicate: 0, reorder: 0);
            using var cancellation = new CancellationTokenSource (TimeSpan.FromMilliseconds (200));

            Assert.CatchAsync<OperationCanceledException> (() => link.A.ConnectAsync (LossyUtpLink.EndPointB, cancellation.Token));
        }

        [Test]
        public void ConnectFailsWhenIncomingDisabled ()
        {
            using var link = new LossyUtpLink (drop: 0, duplicate: 0, reorder: 0);
            link.A.ConnectTimeout = TimeSpan.FromSeconds (1);
            link.B.AcceptIncoming = false;

            Assert.ThrowsAsync<TimeoutException> (() => link.A.ConnectAsync (LossyUtpLink.EndPointB, CancellationToken.None));
        }

        [Test]
        public void ResetForUnknownConnectionId ()
        {
            var sent = new List<byte[]> ();
            using var manager = new UtpSocketManager ((data, remote) => {
                lock (sent)
                    sent.Add (data.ToArray ());
                return default;
            });

            var datagram = new byte[UtpPacketCodec.HeaderLength + 3];
            UtpPacketCodec.Write (datagram, new UtpHeader (UtpPacketType.Data, 4321, 1, 0, 65536, 777, 55), default, new byte[] { 1, 2, 3 });
            manager.HandlePacket (datagram, LossyUtpLink.EndPointA);

            Assert.AreEqual (1, sent.Count);
            Assert.IsTrue (UtpPacketCodec.TryRead (sent[0], out var reset, out _, out _));
            Assert.AreEqual (UtpPacketType.Reset, reset.Type);
            Assert.AreEqual (4321, reset.ConnectionId);
            Assert.AreEqual (777, reset.AckNumber);

            var resetDatagram = new byte[UtpPacketCodec.HeaderLength];
            UtpPacketCodec.Write (resetDatagram, new UtpHeader (UtpPacketType.Reset, 1, 1, 0, 0, 1, 1), default, default);
            manager.HandlePacket (resetDatagram, LossyUtpLink.EndPointA);
            Assert.AreEqual (1, sent.Count);
        }

        [Test]
        public async Task ResetFromPeerFailsPendingReceive ()
        {
            using var link = new LossyUtpLink (drop: 0, duplicate: 0, reorder: 0);
            var (client, server) = await ConnectPair (link);

            var pending = client.ReceiveAsync (new byte[16]).AsTask ();
            link.B.Dispose ();

            Assert.ThrowsAsync<IOException> (async () => await pending.WithTimeout (TestTimeout));
            Assert.ThrowsAsync<IOException> (async () => await client.SendAsync (new byte[16]));
        }

        [Test]
        public async Task DuplicateSynIsAnsweredOnce ()
        {
            using var link = new LossyUtpLink (drop: 0, duplicate: 1, reorder: 0);
            int accepted = 0;
            link.B.ConnectionAccepted += c => Interlocked.Increment (ref accepted);

            using var client = await link.A.ConnectAsync (LossyUtpLink.EndPointB, CancellationToken.None).WithTimeout (TestTimeout);
            await Task.Delay (200);
            Assert.AreEqual (1, accepted);
        }

        [Test]
        public async Task DisposeReleasesPendingCalls ()
        {
            using var link = new LossyUtpLink (drop: 0, duplicate: 0, reorder: 0);
            var (client, server) = await ConnectPair (link);

            var pendingReceive = client.ReceiveAsync (new byte[16]).AsTask ();
            client.Dispose ();

            Assert.AreEqual (0, await pendingReceive.WithTimeout (TestTimeout));
            Assert.ThrowsAsync<ObjectDisposedException> (async () => await client.SendAsync (new byte[16]));
            Assert.AreEqual (0, await server.ReceiveAsync (new byte[16]).AsTask ().WithTimeout (TestTimeout));
        }

        [Test]
        public async Task SendBlocksWhileSendBufferIsFull ()
        {
            using var link = new LossyUtpLink (drop: 0, duplicate: 0, reorder: 0);
            var (client, server) = await ConnectPair (link);
            var data = CreateData (6 * 1024 * 1024, 4);

            var send = client.SendAsync (data).AsTask ();
            await Task.Delay (300);
            Assert.IsFalse (send.IsCompleted);

            var received = await ReceiveExactly (server, data.Length).WithTimeout (TestTimeout);
            await send.WithTimeout (TestTimeout);
            AssertSameBytes (data, received);
        }

        internal static async Task<(UtpConnection Client, UtpConnection Server)> ConnectPair (LossyUtpLink link)
        {
            var accepted = new TaskCompletionSource<UtpConnection> (TaskCreationOptions.RunContinuationsAsynchronously);
            link.B.ConnectionAccepted += c => accepted.TrySetResult (c);

            var client = await link.A.ConnectAsync (LossyUtpLink.EndPointB, CancellationToken.None).WithTimeout (TestTimeout);
            var server = await accepted.Task.WithTimeout (TestTimeout);
            return (client, server);
        }

        internal static void AssertSameBytes (byte[] expected, byte[] actual)
            => Assert.IsTrue (expected.AsSpan ().SequenceEqual (actual), "Received bytes differ from the bytes sent");

        internal static byte[] CreateData (int length, int seed)
        {
            var data = new byte[length];
            new Random (seed).NextBytes (data);
            return data;
        }

        internal static async Task SendAll (UtpConnection connection, byte[] data)
        {
            var random = new Random (data.Length);
            int offset = 0;
            while (offset < data.Length) {
                int count = Math.Min (random.Next (1, 64 * 1024), data.Length - offset);
                await connection.SendAsync (data.AsMemory (offset, count));
                offset += count;
            }
        }

        internal static async Task<byte[]> ReceiveExactly (UtpConnection connection, int length)
        {
            var result = new byte[length];
            int offset = 0;
            while (offset < length) {
                int read = await connection.ReceiveAsync (result.AsMemory (offset, Math.Min (length - offset, 16 * 1024)));
                if (read == 0)
                    throw new EndOfStreamException ($"Connection closed after {offset} bytes");
                offset += read;
            }
            return result;
        }

        static async Task WaitUntil (Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew ();
            while (!condition ()) {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds (5))
                    Assert.Fail ("Condition was not met in time");
                await Task.Delay (20);
            }
        }
    }
}
