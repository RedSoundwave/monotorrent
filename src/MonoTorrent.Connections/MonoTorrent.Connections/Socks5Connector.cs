using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ReusableTasks;

namespace MonoTorrent.Connections
{
    /// <summary>BEP 704 / #704: Routes peer connections through a SOCKS5 proxy.</summary>
    public class Socks5Connector : ISocketConnector
    {
        const byte Version = 0x05;
        const byte NoAuthentication = 0x00;
        const byte UserNamePassword = 0x02;
        const byte NoAcceptableMethod = 0xFF;

        readonly IPEndPoint proxyEndPoint;
        readonly NetworkCredential? credentials;

        public Socks5Connector (IPEndPoint proxyEndPoint, NetworkCredential? credentials = null)
        {
            this.proxyEndPoint = proxyEndPoint ?? throw new ArgumentNullException (nameof (proxyEndPoint));
            this.credentials = string.IsNullOrEmpty (credentials?.UserName) ? null : credentials;
        }

        public async ReusableTask<Socket> ConnectAsync (Uri uri, CancellationToken token)
        {
            var target = IPAddress.Parse (uri.Host);
            var socket = new Socket (proxyEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try {
                await socket.ConnectAsync (proxyEndPoint, token).ConfigureAwait (false);
                await NegotiateAsync (socket, token).ConfigureAwait (false);
                await RequestConnectAsync (socket, target, uri.Port, token).ConfigureAwait (false);
            } catch {
                socket.Dispose ();
                throw;
            }
            return socket;
        }

        async Task NegotiateAsync (Socket socket, CancellationToken token)
        {
            var greeting = credentials is null
                ? new byte[] { Version, 0x01, NoAuthentication }
                : new byte[] { Version, 0x02, NoAuthentication, UserNamePassword };
            await SendAllAsync (socket, greeting, token).ConfigureAwait (false);

            var choice = new byte[2];
            await ReceiveAllAsync (socket, choice, token).ConfigureAwait (false);
            if (choice[0] != Version || choice[1] == NoAcceptableMethod)
                throw new IOException ($"SOCKS5 proxy rejected the authentication methods (method={choice[1]})");

            if (choice[1] == UserNamePassword)
                await AuthenticateAsync (socket, token).ConfigureAwait (false);
            else if (choice[1] != NoAuthentication)
                throw new IOException ($"SOCKS5 proxy chose an unsupported method ({choice[1]})");
        }

        async Task AuthenticateAsync (Socket socket, CancellationToken token)
        {
            if (credentials is null)
                throw new IOException ("SOCKS5 proxy requires a user name and password");

            var user = Encoding.UTF8.GetBytes (credentials.UserName);
            var password = Encoding.UTF8.GetBytes (credentials.Password ?? string.Empty);
            if (user.Length > 255 || password.Length > 255)
                throw new IOException ("SOCKS5 user name and password must be at most 255 bytes");

            var request = new byte[3 + user.Length + password.Length];
            request[0] = 0x01;
            request[1] = (byte) user.Length;
            user.CopyTo (request, 2);
            request[2 + user.Length] = (byte) password.Length;
            password.CopyTo (request, 3 + user.Length);
            await SendAllAsync (socket, request, token).ConfigureAwait (false);

            var reply = new byte[2];
            await ReceiveAllAsync (socket, reply, token).ConfigureAwait (false);
            if (reply[1] != 0x00)
                throw new IOException ("SOCKS5 proxy rejected the user name or password");
        }

        static async Task RequestConnectAsync (Socket socket, IPAddress target, int port, CancellationToken token)
        {
            var address = target.GetAddressBytes ();
            var request = new byte[6 + address.Length];
            request[0] = Version;
            request[1] = 0x01;
            request[3] = target.AddressFamily == AddressFamily.InterNetwork ? (byte) 0x01 : (byte) 0x04;
            address.CopyTo (request, 4);
            BinaryPrimitives.WriteUInt16BigEndian (request.AsSpan (request.Length - 2), (ushort) port);
            await SendAllAsync (socket, request, token).ConfigureAwait (false);

            var header = new byte[4];
            await ReceiveAllAsync (socket, header, token).ConfigureAwait (false);
            if (header[1] != 0x00)
                throw new IOException ($"SOCKS5 CONNECT failed (reply={header[1]})");

            int boundLength = header[3] switch {
                0x01 => 4,
                0x04 => 16,
                0x03 => await ReceiveDomainLengthAsync (socket, token).ConfigureAwait (false),
                _ => throw new IOException ($"Unknown SOCKS5 address type {header[3]}")
            };
            await ReceiveAllAsync (socket, new byte[boundLength + 2], token).ConfigureAwait (false);
        }

        static async Task<int> ReceiveDomainLengthAsync (Socket socket, CancellationToken token)
        {
            var length = new byte[1];
            await ReceiveAllAsync (socket, length, token).ConfigureAwait (false);
            return length[0];
        }

        static async Task SendAllAsync (Socket socket, byte[] data, CancellationToken token)
        {
            int sent = 0;
            while (sent < data.Length)
                sent += await socket.SendAsync (new ArraySegment<byte> (data, sent, data.Length - sent), SocketFlags.None, token).ConfigureAwait (false);
        }

        static async Task ReceiveAllAsync (Socket socket, byte[] buffer, CancellationToken token)
        {
            int received = 0;
            while (received < buffer.Length) {
                var count = await socket.ReceiveAsync (new ArraySegment<byte> (buffer, received, buffer.Length - received), SocketFlags.None, token).ConfigureAwait (false);
                if (count == 0)
                    throw new IOException ("SOCKS5 proxy closed the connection");
                received += count;
            }
        }
    }
}
