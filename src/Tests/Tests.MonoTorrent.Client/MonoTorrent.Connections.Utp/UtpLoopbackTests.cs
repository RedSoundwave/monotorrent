using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using MonoTorrent.Connections.Peer;

using NUnit.Framework;

namespace MonoTorrent.Connections.Utp
{
    [TestFixture]
    public class UtpLoopbackTests
    {
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds (20);

        sealed class Endpoint : IDisposable
        {
            readonly CancellationTokenSource cancellation = new CancellationTokenSource ();
            readonly Task receiveLoop;

            public Endpoint ()
            {
                Socket = new Socket (AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                Socket.ReceiveBufferSize = 4 * 1024 * 1024;
                Socket.Bind (new IPEndPoint (IPAddress.Loopback, 0));
                Manager = new UtpSocketManager (SendAsync);
                receiveLoop = Task.Run (ReceiveLoop);
            }

            public UtpSocketManager Manager { get; }

            public Socket Socket { get; }

            public IPEndPoint LocalEndPoint => (IPEndPoint) Socket.LocalEndPoint;

            async ValueTask SendAsync (ReadOnlyMemory<byte> data, IPEndPoint remote)
                => await Socket.SendToAsync (data, SocketFlags.None, remote).ConfigureAwait (false);

            async Task ReceiveLoop ()
            {
                var buffer = new byte[2048];
                EndPoint any = new IPEndPoint (IPAddress.Any, 0);
                while (!cancellation.IsCancellationRequested) {
                    try {
                        var result = await Socket.ReceiveFromAsync (buffer, SocketFlags.None, any, cancellation.Token).ConfigureAwait (false);
                        if (UtpSocketManager.IsUtpPacket (buffer.AsSpan (0, result.ReceivedBytes)))
                            Manager.HandlePacket (buffer.AsSpan (0, result.ReceivedBytes), (IPEndPoint) result.RemoteEndPoint);
                    } catch (SocketException) {
                    } catch (OperationCanceledException) {
                        return;
                    } catch (ObjectDisposedException) {
                        return;
                    }
                }
            }

            public void Dispose ()
            {
                Manager.Dispose ();
                cancellation.Cancel ();
                Socket.Dispose ();
                receiveLoop.Wait (TimeSpan.FromSeconds (5));
            }
        }

        [Test]
        public async Task TransferOverRealUdpSockets ()
        {
            using var first = new Endpoint ();
            using var second = new Endpoint ();
            var accepted = new TaskCompletionSource<UtpConnection> (TaskCreationOptions.RunContinuationsAsynchronously);
            second.Manager.ConnectionAccepted += c => accepted.TrySetResult (c);

            using var client = await first.Manager.ConnectAsync (second.LocalEndPoint, CancellationToken.None).WithTimeout (TestTimeout);
            using var server = await accepted.Task.WithTimeout (TestTimeout);
            Assert.AreEqual (first.LocalEndPoint, server.RemoteEndPoint);

            var upload = UtpConnectionTests.CreateData (1024 * 1024, 10);
            var download = UtpConnectionTests.CreateData (1024 * 1024, 11);
            var receivedByServer = UtpConnectionTests.ReceiveExactly (server, upload.Length);
            var receivedByClient = UtpConnectionTests.ReceiveExactly (client, download.Length);
            await Task.WhenAll (
                UtpConnectionTests.SendAll (client, upload),
                UtpConnectionTests.SendAll (server, download),
                receivedByServer,
                receivedByClient).WithTimeout (TestTimeout);

            UtpConnectionTests.AssertSameBytes (upload, await receivedByServer);
            UtpConnectionTests.AssertSameBytes (download, await receivedByClient);
        }

        [Test]
        public async Task PeerConnectionAdapter ()
        {
            using var first = new Endpoint ();
            using var second = new Endpoint ();
            var accepted = new TaskCompletionSource<UtpConnection> (TaskCreationOptions.RunContinuationsAsynchronously);
            second.Manager.ConnectionAccepted += c => accepted.TrySetResult (c);

            using var outgoing = new UtpPeerConnection (new Uri ($"ipv4://{second.LocalEndPoint}"), first.Manager);
            Assert.IsFalse (outgoing.IsIncoming);
            Assert.IsTrue (outgoing.CanReconnect);
            Assert.AreEqual (second.LocalEndPoint, outgoing.EndPoint);
            Assert.IsTrue (await outgoing.ConnectAsync ().AsTask ().WithTimeout (TestTimeout));

            using var incoming = new UtpPeerConnection (await accepted.Task.WithTimeout (TestTimeout));
            Assert.IsTrue (incoming.IsIncoming);
            Assert.IsFalse (incoming.CanReconnect);
            Assert.AreEqual (new Uri ($"ipv4://{first.LocalEndPoint}"), incoming.Uri);
            CollectionAssert.AreEqual (IPAddress.Loopback.GetAddressBytes (), incoming.AddressBytes.ToArray ());

            var message = UtpConnectionTests.CreateData (100_000, 12);
            Assert.AreEqual (message.Length, await outgoing.SendAsync (message).AsTask ().WithTimeout (TestTimeout));
            var received = new byte[message.Length];
            int offset = 0;
            while (offset < received.Length)
                offset += await incoming.ReceiveAsync (received.AsMemory (offset)).AsTask ().WithTimeout (TestTimeout);
            UtpConnectionTests.AssertSameBytes (message, received);

            outgoing.Dispose ();
            Assert.IsTrue (outgoing.Disposed);
            Assert.AreEqual (0, await incoming.ReceiveAsync (new byte[16]).AsTask ().WithTimeout (TestTimeout));
        }

        [Test]
        public async Task PeerConnectionAdapterReturnsFalseOnFailure ()
        {
            using var first = new Endpoint ();
            using var silent = new Socket (AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            silent.Bind (new IPEndPoint (IPAddress.Loopback, 0));
            first.Manager.ConnectTimeout = TimeSpan.FromSeconds (1);

            using var outgoing = new UtpPeerConnection (new Uri ($"ipv4://{silent.LocalEndPoint}"), first.Manager);
            Assert.IsFalse (await outgoing.ConnectAsync ().AsTask ().WithTimeout (TestTimeout));
        }
    }
}
