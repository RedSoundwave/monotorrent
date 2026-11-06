//
// Node.cs
//
// Authors:
//   Jérémie Laval <jeremie.laval@gmail.com>
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2008 Jérémie Laval, Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using MonoTorrent.BEncoding;
using MonoTorrent.Messages;

namespace MonoTorrent.Dht
{
    class Node : IEquatable<Node>
    {
        public static readonly int MaxFailures = 2;
        public CompactEndPoint EndPoint { get; }
        public int FailedCount { get; set; }
        public NodeId Id { get; }
        public TimeSpan LastSeen => LastSeenTimer.Elapsed + LastSeenDelta;
        TimeSpan LastSeenDelta { get; set; }
        ValueStopwatch LastSeenTimer;
        public NodeState State {
            get {
                if (FailedCount >= MaxFailures)
                    return NodeState.Bad;

                else if (!LastSeenTimer.IsRunning)
                    return NodeState.Unknown;

                return LastSeen.TotalMinutes < 15 ? NodeState.Good : NodeState.Questionable;
            }
        }
        public BEncodedValue? Token { get; set; }

        public Node (NodeId id, CompactEndPoint endPoint)
        {
            EndPoint = endPoint;
            Id = id;

            LastSeenDelta = TimeSpan.FromDays (1);
            LastSeenTimer = new ValueStopwatch ();
        }

        internal void Seen ()
        {
            Seen (TimeSpan.Zero);
        }

        internal void Seen (TimeSpan delta)
        {
            FailedCount = 0;
            LastSeenDelta = delta;
            LastSeenTimer.Restart ();
        }

        internal int CompactNodeLength => Id.Span.Length + EndPoint.Address.Length + 2;

        internal static int CompactNodeSize (AddressFamily family)
            => family == AddressFamily.InterNetworkV6 ? 38 : 26;

        internal BEncodedString CompactEndPoint ()
        {
            byte[] buffer = new byte[EndPoint.Address.Length + 2];
            if (CompactEndPoint (buffer) != buffer.Length)
                throw new InvalidOperationException ("Couldn't write the address to the provided buffer");
            return new BEncodedString (buffer);
        }

        internal int CompactEndPoint (Span<byte> buffer)
        {
            if (!EndPoint.TryWriteBytes (buffer, out int written))
                throw new InvalidOperationException ("Couldn't write the address to the provided buffer");
            return written;
        }

        internal static BEncodedString CompactEndPoint (IList<Node> peers)
        {
            int length = 0;
            for (int i = 0; i < peers.Count; i++)
                length += peers[i].EndPoint.Address.Length + 2;

            var buffer = new byte[length];
            int offset = 0;
            for (int i = 0; i < peers.Count; i++)
                offset += peers[i].CompactEndPoint (buffer.AsSpan (offset));

            return new BEncodedString (buffer);
        }

        internal BEncodedString CompactNode ()
        {
            byte[] buffer = new byte[CompactNodeLength];
            CompactNode (buffer);
            return new BEncodedString (buffer);
        }

        internal int CompactNode (Span<byte> buffer)
        {
            Message.Write (ref buffer, Id.Span);
            return CompactEndPoint (buffer) + Id.Span.Length;
        }

        internal static BEncodedString CompactNode (ICollection<Node> nodes)
        {
            int length = 0;
            foreach (Node node in nodes)
                length += node.CompactNodeLength;

            byte[] buffer = new byte[length];
            int offset = 0;
            foreach (Node node in nodes)
                offset += node.CompactNode (buffer.AsSpan (offset, node.CompactNodeLength));

            return new BEncodedString (buffer);
        }

        internal static Node? FromCompactNode (ReadOnlySpan<byte> buffer)
            => FromCompactNode (buffer, AddressFamily.InterNetwork);

        internal static Node? FromCompactNode (ReadOnlySpan<byte> buffer, AddressFamily family)
        {
            int size = CompactNodeSize (family);
            if (buffer.Length != size)
                throw new ArgumentException ($"buffer must be exactly {size} bytes long", nameof (buffer));

            var address = buffer.Slice (20, size - 22);
            if (family == AddressFamily.InterNetwork ? IsObviouslyBad (address) : IsObviouslyBadIPv6 (address))
                return null;

            var endpoint = MonoTorrent.CompactEndPoint.FromCompact (buffer.Slice (20), family);
            return new Node (new NodeId (buffer.Slice (0, 20)), endpoint);
        }

        static bool IsObviouslyBad (ReadOnlySpan<byte> ipBytes)
        {
            return ipBytes[0] switch {
                0 => true,
                10 => true,
                127 => true,
                >= 224 => true, // multicast + reserved + 255.*
                _ => false
            };
        }

        static bool IsObviouslyBadIPv6 (ReadOnlySpan<byte> ipBytes)
        {
            if (ipBytes[0] == 0xff)
                return true;
            if (ipBytes[0] == 0xfe && (ipBytes[1] & 0xc0) == 0x80)
                return true;
            return ipBytes.Slice (0, 10).IndexOfAnyExcept ((byte) 0) < 0;
        }

        internal static IEnumerable<Node> FromCompactNode (IEnumerable<ReadOnlyMemory<byte>> nodes)
            => FromCompactNode (nodes, AddressFamily.InterNetwork);

        internal static IEnumerable<Node> FromCompactNode (IEnumerable<ReadOnlyMemory<byte>> nodes, AddressFamily family)
        {
            int size = CompactNodeSize (family);
            foreach (var rawNode in nodes) {
                for (int i = 0; (i + size) <= rawNode.Length; i += size) {
                    var n = FromCompactNode (rawNode.Span.Slice (i, size), family);
                    if (n is not null)
                        yield return n;
                }
            }
        }

        internal static IEnumerable<Node> FromCompactNodes (BEncodedString nodes)
        {
            foreach (var v in FromCompactNodes (nodes.Span))
                yield return v;
        }

        internal static IEnumerable<Node> FromCompactNodes (ReadOnlySpan<byte> nodes)
            => FromCompactNodes (nodes, AddressFamily.InterNetwork);

        internal static IEnumerable<Node> FromCompactNodes (ReadOnlySpan<byte> nodes, AddressFamily family)
        {
            int size = CompactNodeSize (family);
            var results = new List<Node> (nodes.Length / size);
            while (nodes.Length >= size) {
                var node = FromCompactNode (nodes.Slice (0, size), family);
                if (node is not null)
                    results.Add (node);
                nodes = nodes.Slice (size);
            }
            return results;
        }

        public override bool Equals (object? obj)
        {
            return Equals (obj as Node);
        }

        public bool Equals (Node? other)
            => other is null ? false : Id.Equals (other.Id);

        public override int GetHashCode ()
        {
            return Id.GetHashCode ();
        }

        public override string ToString ()
        {
            var sb = new StringBuilder (48);
            for (int i = 0; i < Id.Span.Length; i++) {
                sb.Append (Id.Span[i]);
                sb.Append ("-");
            }
            return sb.ToString (0, sb.Length - 1);
        }
    }
}
