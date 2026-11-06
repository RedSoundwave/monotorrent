using System;
using System.Net;

using MonoTorrent.Connections.Dht;

namespace MonoTorrent.Connections.Peer
{
    public interface IUtpSocket : IDisposable
    {
        IDhtListener DhtListener { get; }

        IPeerConnectionListener PeerListener { get; }

        IPEndPoint PreferredLocalEndPoint { get; }

        IPeerConnection CreateConnection (Uri uri);
    }
}
