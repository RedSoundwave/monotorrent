using System.Net;

namespace MonoTorrent.Connections.Utp
{
    readonly record struct UtpConnectionKey (IPEndPoint EndPoint, ushort ConnectionId);
}
