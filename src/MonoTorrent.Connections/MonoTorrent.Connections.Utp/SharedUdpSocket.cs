using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using MonoTorrent.Connections.Dht;
using MonoTorrent.Connections.Peer;

namespace MonoTorrent.Connections.Utp
{
    public sealed class SharedUdpSocket : IUtpSocket
    {
        const int SioUdpConnectionReset = -1744830452;

        readonly object locker = new object ();
        readonly SharedDhtListener dhtListener;
        readonly SharedUtpPeerListener peerListener;
        Socket? socket;
        CancellationTokenSource? cancellation;
        int users;

        public SharedUdpSocket (IPEndPoint endPoint)
        {
            PreferredLocalEndPoint = endPoint ?? throw new ArgumentNullException (nameof (endPoint));
            Utp = new UtpSocketManager (SendAsync) { AcceptIncoming = false };
            dhtListener = new SharedDhtListener (this);
            peerListener = new SharedUtpPeerListener (this);
            Utp.ConnectionAccepted += peerListener.Accept;
        }

        public IDhtListener DhtListener => dhtListener;

        public IPEndPoint? LocalEndPoint { get; private set; }

        public IPeerConnectionListener PeerListener => peerListener;

        public IPEndPoint PreferredLocalEndPoint { get; }

        public UtpSocketManager Utp { get; }

        public IPeerConnection CreateConnection (Uri uri)
            => new UtpPeerConnection (uri, Utp);

        internal ListenerStatus Acquire ()
        {
            lock (locker) {
                if (socket == null && !TryOpen ())
                    return ListenerStatus.PortNotFree;
                users++;
                return ListenerStatus.Listening;
            }
        }

        internal void Release ()
        {
            lock (locker) {
                if (users == 0 || --users > 0)
                    return;
                Close ();
            }
        }

        internal void SetAcceptIncoming (bool accept)
            => Utp.AcceptIncoming = accept;

        internal async ValueTask SendAsync (ReadOnlyMemory<byte> datagram, IPEndPoint endPoint)
        {
            var current = socket;
            if (current == null)
                return;
            try {
                await current.SendToAsync (datagram, SocketFlags.None, endPoint).ConfigureAwait (false);
            } catch (SocketException) {
            } catch (ObjectDisposedException) {
            }
        }

        public void Dispose ()
        {
            lock (locker) {
                users = 0;
                Close ();
            }
            Utp.Dispose ();
        }

        bool TryOpen ()
        {
            var family = PreferredLocalEndPoint.AddressFamily;
            var created = new Socket (family, SocketType.Dgram, ProtocolType.Udp);
            try {
                if (family == AddressFamily.InterNetworkV6)
                    created.DualMode = false;
                if (OperatingSystem.IsWindows ())
                    created.IOControl (SioUdpConnectionReset, new byte[4], null);
                created.Bind (PreferredLocalEndPoint);
            } catch (SocketException) {
                created.Dispose ();
                return false;
            }

            socket = created;
            LocalEndPoint = (IPEndPoint?) created.LocalEndPoint;
            cancellation = new CancellationTokenSource ();
            _ = ReceiveLoopAsync (created, cancellation.Token);
            return true;
        }

        void Close ()
        {
            cancellation?.Cancel ();
            cancellation = null;
            socket?.Dispose ();
            socket = null;
            LocalEndPoint = null;
        }

        async Task ReceiveLoopAsync (Socket receiver, CancellationToken token)
        {
            var family = PreferredLocalEndPoint.AddressFamily;
            var buffer = new byte[64 * 1024];
            var from = new SocketAddress (family);
            var template = new IPEndPoint (family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);

            while (!token.IsCancellationRequested) {
                int count;
                try {
                    count = await receiver.ReceiveFromAsync (buffer, SocketFlags.None, from, token).ConfigureAwait (false);
                } catch (OperationCanceledException) {
                    return;
                } catch (ObjectDisposedException) {
                    return;
                } catch (SocketException) {
                    continue;
                }

                if (count == 0 || token.IsCancellationRequested)
                    continue;

                try {
                    Dispatch (buffer.AsSpan (0, count), from, template);
                } catch (Exception ex) when (ex is not OutOfMemoryException) {
                }
            }
        }

        void Dispatch (ReadOnlySpan<byte> datagram, SocketAddress from, IPEndPoint template)
        {
            if (UtpSocketManager.IsUtpPacket (datagram))
                Utp.HandlePacket (datagram, (IPEndPoint) template.Create (from));
            else
                dhtListener.Receive (datagram.ToArray (), new CompactEndPoint (from));
        }
    }
}
