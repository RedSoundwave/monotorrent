using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using MonoTorrent.Connections.Utp;

using ReusableTasks;

namespace MonoTorrent.Connections.Peer
{
    public sealed class UtpPeerConnection : IPeerConnection
    {
        public ReadOnlyMemory<byte> AddressBytes { get; }

        public bool CanReconnect => !IsIncoming;

        CancellationTokenSource Cancellation { get; }

        UtpConnection? Connection { get; set; }

        public bool Disposed { get; private set; }

        public IPEndPoint EndPoint { get; }

        public bool IsIncoming { get; }

        UtpSocketManager? Manager { get; }

        public Uri Uri { get; }

        public UtpPeerConnection (Uri uri, UtpSocketManager manager)
            : this (uri, manager ?? throw new ArgumentNullException (nameof (manager)), null)
        {

        }

        public UtpPeerConnection (UtpConnection accepted)
            : this (CreateUri ((accepted ?? throw new ArgumentNullException (nameof (accepted))).RemoteEndPoint), null, accepted)
        {

        }

        UtpPeerConnection (Uri uri, UtpSocketManager? manager, UtpConnection? connection)
        {
            Cancellation = new CancellationTokenSource ();
            Connection = connection;
            EndPoint = new IPEndPoint (IPAddress.Parse (uri.Host), uri.Port);
            AddressBytes = EndPoint.Address.GetAddressBytes ();
            IsIncoming = connection != null;
            Manager = manager;
            Uri = uri;
        }

        static Uri CreateUri (IPEndPoint endPoint)
            => endPoint.AddressFamily switch {
                AddressFamily.InterNetwork => new Uri ($"ipv4://{endPoint}"),
                AddressFamily.InterNetworkV6 => new Uri ($"ipv6://{endPoint}"),
                _ => throw new NotSupportedException ($"AddressFamily.{endPoint.AddressFamily} is unsupported")
            };

        public async ReusableTask<bool> ConnectAsync ()
        {
            if (Manager == null)
                throw new InvalidOperationException ("This connection represents an incoming connection");

            try {
                var connection = await Manager.ConnectAsync (EndPoint, Cancellation.Token).ConfigureAwait (false);
                if (Disposed) {
                    connection.Dispose ();
                    return false;
                }
                Connection = connection;
            } catch {
                return false;
            }
            return true;
        }

        public async ReusableTask<int> ReceiveAsync (Memory<byte> buffer)
        {
            var connection = Connection ?? throw new InvalidOperationException ("The uTP connection is not established");
            return await connection.ReceiveAsync (buffer).ConfigureAwait (false);
        }

        public async ReusableTask<int> SendAsync (ReadOnlyMemory<byte> buffer)
        {
            var connection = Connection ?? throw new InvalidOperationException ("The uTP connection is not established");
            await connection.SendAsync (buffer).ConfigureAwait (false);
            return buffer.Length;
        }

        public void Dispose ()
        {
            Disposed = true;
            Cancellation.Cancel ();
            Connection?.Dispose ();
        }
    }
}
