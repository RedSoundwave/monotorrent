// BEP 42: DHT Security Extension
// https://www.bittorrent.org/beps/bep_0042.html
//
// Node IDs are constrained by the client's external IP address using CRC32C.
// This prevents Sybil attacks by making it hard to choose arbitrary node IDs.

using System;
using System.Net;
using System.Net.Sockets;

namespace MonoTorrent.Dht
{
    static class Bep42
    {
        static readonly byte[] Ipv4Mask = { 0x03, 0x0f, 0x3f, 0xff };
        static readonly byte[] Ipv6Mask = { 0x01, 0x03, 0x07, 0x0f, 0x1f, 0x3f, 0x7f, 0xff };

        /// <summary>Generate a BEP 42-compliant node ID from the given external IP address.</summary>
        public static NodeId CreateNodeId (IPAddress externalIp)
        {
            byte r = (byte) Random.Shared.Next (256);
            uint crc = ComputeCrc (externalIp, r);

            Span<byte> id = stackalloc byte[20];
            id[0] = (byte) (crc >> 24);
            id[1] = (byte) (crc >> 16);
            id[2] = (byte) (((crc >> 8) & 0xf8) | (Random.Shared.Next (8)));
            Random.Shared.NextBytes (id.Slice (3, 16));
            id[19] = r;
            return new NodeId (id);
        }

        /// <summary>Returns true if the node ID is valid for the given IP address (BEP 42).</summary>
        public static bool IsValidNodeId (ReadOnlySpan<byte> nodeId, IPAddress remoteIp)
        {
            if (nodeId.Length != 20)
                return false;
            byte r = nodeId[19];
            uint crc = ComputeCrc (remoteIp, r);
            return nodeId[0] == (byte) (crc >> 24)
                && nodeId[1] == (byte) (crc >> 16)
                && (nodeId[2] & 0xf8) == ((byte) (crc >> 8) & 0xf8);
        }

        static uint ComputeCrc (IPAddress ip, byte r)
        {
            var raw = ip.GetAddressBytes ();
            bool isV4 = ip.AddressFamily == AddressFamily.InterNetwork;
            int len = isV4 ? 4 : 8;
            Span<byte> input = stackalloc byte[len];
            raw.AsSpan (0, len).CopyTo (input);

            if (isV4) {
                for (int i = 0; i < 4; i++)
                    input[i] = (byte) ((input[i] & Ipv4Mask[i]) ^ (i < 3 ? r >> 5 : r));
            } else {
                for (int i = 0; i < 8; i++)
                    input[i] = (byte) ((input[i] & Ipv6Mask[i]) ^ (i < 7 ? r >> 5 : r));
            }

            return Crc32C (input);
        }

        // CRC32C (Castagnoli) software implementation — polynomial 0x82F63B78 (reversed)
        static uint Crc32C (ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc >> 1) ^ (0x82F63B78u & (uint) -(int) (crc & 1));
            }
            return ~crc;
        }
    }
}
