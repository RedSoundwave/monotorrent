using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using ReusableTasks;

namespace MonoTorrent.Connections
{
    /// <summary>BEP 704 / #704: Routes peer connections through a SOCKS5 proxy.</summary>
    public class Socks5Connector : ISocketConnector
    {
        readonly IPEndPoint proxyEndPoint;

        public Socks5Connector (IPEndPoint proxyEndPoint)
            => this.proxyEndPoint = proxyEndPoint ?? throw new ArgumentNullException (nameof (proxyEndPoint));

        public async ReusableTask<Socket> ConnectAsync (Uri uri, CancellationToken token)
        {
            bool isIPv6 = uri.Scheme == "ipv6";
            var target = IPAddress.Parse (uri.Host);
            int port = uri.Port;

            var socket = new Socket (proxyEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try {
                await socket.ConnectAsync (proxyEndPoint, token).ConfigureAwait (false);

                // Greeting: no-auth only
                var greeting = new byte[] { 0x05, 0x01, 0x00 };
                await SendAllAsync (socket, greeting, token).ConfigureAwait (false);

                var serverChoice = new byte[2];
                await RecvAllAsync (socket, serverChoice, token).ConfigureAwait (false);
                if (serverChoice[0] != 0x05 || serverChoice[1] != 0x00)
                    throw new Exception ($"SOCKS5 proxy rejected: method={serverChoice[1]}");

                // Connect request
                byte[] request;
                if (target.AddressFamily == AddressFamily.InterNetwork) {
                    request = new byte[10];
                    request[0] = 0x05; request[1] = 0x01; request[2] = 0x00; request[3] = 0x01;
                    target.GetAddressBytes ().CopyTo (request, 4);
                } else {
                    request = new byte[22];
                    request[0] = 0x05; request[1] = 0x01; request[2] = 0x00; request[3] = 0x04;
                    target.GetAddressBytes ().CopyTo (request, 4);
                }
                BinaryPrimitives.WriteUInt16BigEndian (request.AsSpan (request.Length - 2), (ushort) port);
                await SendAllAsync (socket, request, token).ConfigureAwait (false);

                // Response header: VER REP RSV ATYP
                var respHdr = new byte[4];
                await RecvAllAsync (socket, respHdr, token).ConfigureAwait (false);
                if (respHdr[1] != 0x00)
                    throw new Exception ($"SOCKS5 CONNECT failed: rep={respHdr[1]}");

                // Consume bound address
                int addrLen = respHdr[3] switch { 0x01 => 4, 0x04 => 16, 0x03 => throw new Exception ("SOCKS5 returned domain-type bound address"), _ => throw new Exception ($"Unknown SOCKS5 ATYP={respHdr[3]}") };
                var tail = new byte[addrLen + 2];
                await RecvAllAsync (socket, tail, token).ConfigureAwait (false);

            } catch {
                socket.Dispose ();
                throw;
            }
            return socket;
        }

        static async Task SendAllAsync (Socket socket, byte[] data, CancellationToken token)
        {
            int sent = 0;
            while (sent < data.Length)
                sent += await socket.SendAsync (new ArraySegment<byte> (data, sent, data.Length - sent), SocketFlags.None, token).ConfigureAwait (false);
        }

        static async Task RecvAllAsync (Socket socket, byte[] buffer, CancellationToken token)
        {
            int received = 0;
            while (received < buffer.Length)
                received += await socket.ReceiveAsync (new ArraySegment<byte> (buffer, received, buffer.Length - received), SocketFlags.None, token).ConfigureAwait (false);
        }
    }
}
