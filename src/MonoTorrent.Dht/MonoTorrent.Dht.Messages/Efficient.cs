using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using MonoTorrent.BEncoding;

namespace MonoTorrent.Dht.Messages
{
    static class Krpc
    {
        internal static class Queries
        {
            public static ReadOnlySpan<byte> AnnouncePeer => "announce_peer"u8;
            public static ReadOnlySpan<byte> FindNode => "find_node"u8;
            public static ReadOnlySpan<byte> GetPeers => "get_peers"u8;
            public static ReadOnlySpan<byte> Ping => "ping"u8;
        }

        internal static class MessageType
        {
            public static ReadOnlySpan<byte> Query => "q"u8;
            public static ReadOnlySpan<byte> Response => "r"u8;
            public static ReadOnlySpan<byte> Error => "e"u8;

        }

        /// <summary>
        /// Created by the source, echoed back by the receiver.
        /// </summary>
        public static ReadOnlySpan<byte> TransactionIdKey => "t"u8;

        /// <summary>
        /// A BEncodedString which represents the message type. The value is a
        /// BEncoded string set to 'q' for query, 'r' for response, or 'e' for error.
        /// </summary>
        public static ReadOnlySpan<byte> MessageTypeKey => "y"u8;

        /// <summary>
        /// A BEncodedString representing the version of the client which sent this message
        /// </summary>
        public static ReadOnlySpan<byte> ClientIdentifierKey => "v"u8;



        /// <summary>
        /// A BEncodedString specifying the query type (e.g. find_peers, get_peers, announce, ping)
        /// </summary>
        public static ReadOnlySpan<byte> QueryTypeKey => "q"u8;
        /// <summary>
        /// A BEncodedDictionary of arguments sent with a query message
        /// </summary>
        public static ReadOnlySpan<byte> ArgumentsKey => "a"u8;



        /// <summary>
        /// A BEncodedDictionary holding the return values for the original query.
        /// </summary>
        public static ReadOnlySpan<byte> ReturnValuesKey => "r"u8;


        /// <summary>
        /// This is part of all query arguments and response return values.
        /// </summary>
        public static ReadOnlySpan<byte> NodeId => "id"u8;

        /// <summary>
        /// Used in find_node to hold the nodeid of the node being searched for.
        /// </summary>
        public static ReadOnlySpan<byte> Target => "target"u8;
        public static ReadOnlySpan<byte> InfoHash => "info_hash"u8;
        public static ReadOnlySpan<byte> Token => "token"u8;
        public static ReadOnlySpan<byte> Port => "port"u8;
        public static ReadOnlySpan<byte> ImpliedPort => "implied_port"u8;
        public static ReadOnlySpan<byte> Nodes => "nodes"u8;
        public static ReadOnlySpan<byte> Nodes6 => "nodes6"u8;
        public static ReadOnlySpan<byte> Values => "values"u8;
        public static ReadOnlySpan<byte> Want => "want"u8;
        public static ReadOnlySpan<byte> WantNodes4 => "n4"u8;
        public static ReadOnlySpan<byte> WantNodes6 => "n6"u8;
        /// <summary>BEP 43: read-only flag sent by nodes that do not participate fully in routing.</summary>
        public static ReadOnlySpan<byte> ReadOnly => "ro"u8;
    }

    public enum KrpcType
    {
        Unknown,
        Query,
        Error,
        Response
    }

    public enum QueryMethod
    {
        Unknown,
        Ping,
        FindNode,
        GetPeers,
        AnnouncePeer
    }

    [Flags]
    public enum KrpcWant
    {
        None = 0,
        Nodes = 1,
        Nodes6 = 2
    }

    public readonly struct KrpcRequestExtensions
    {
        readonly ReadOnlyMemory<byte> target;
        readonly ReadOnlyMemory<byte> infoHash;
        readonly ReadOnlyMemory<byte> token;

        public readonly ReadOnlySpan<byte> Target => target.Span;
        public readonly ReadOnlySpan<byte> InfoHash => infoHash.Span;
        public readonly ReadOnlySpan<byte> Token => token.Span;

        public readonly int Port;
        public readonly bool ImpliedPort;
        public readonly KrpcWant Want;

        public KrpcRequestExtensions (
            ReadOnlyMemory<byte> target,
            ReadOnlyMemory<byte> infoHash,
            ReadOnlyMemory<byte> token,
            int port,
            bool impliedPort,
            KrpcWant want = KrpcWant.None)
        {
            this.target = target;
            this.infoHash = infoHash;
            this.token = token;
            Port = port;
            ImpliedPort = impliedPort;
            Want = want;
        }
    }

    public readonly struct KrpcResponseExtensions
    {
        readonly ReadOnlyMemory<byte> nodes;
        readonly ReadOnlyMemory<byte> nodes6;
        readonly ReadOnlyMemory<byte> values;
        readonly ReadOnlyMemory<byte> token;

        public readonly ReadOnlySpan<byte> Nodes => nodes.Span;
        public readonly ReadOnlySpan<byte> Nodes6 => nodes6.Span;
        public readonly ReadOnlySpan<byte> Values => values.Span;
        public readonly ReadOnlySpan<byte> Token => token.Span;

        public KrpcResponseExtensions (
            ReadOnlyMemory<byte> nodes,
            ReadOnlyMemory<byte> values,
            ReadOnlyMemory<byte> token,
            ReadOnlyMemory<byte> nodes6 = default)
        {
            this.nodes = nodes;
            this.nodes6 = nodes6;
            this.values = values;
            this.token = token;
        }

        public bool HasNodes => !nodes.IsEmpty;
        public bool HasNodes6 => !nodes6.IsEmpty;
        public bool HasValues => !values.IsEmpty;
    }

    public readonly struct KrpcMessage
    {
        readonly ReadOnlyMemory<byte> transactionId;
        readonly ReadOnlyMemory<byte> nodeId;
        readonly KrpcRequestExtensions request;
        readonly KrpcResponseExtensions response;

        public KrpcType MessageType { get; }
        public QueryMethod QueryMethod { get; }
        public readonly ReadOnlySpan<byte> TransactionId => transactionId.Span;
        public readonly ReadOnlySpan<byte> NodeId => nodeId.Span;
        public readonly KrpcRequestExtensions Request => request;
        public readonly KrpcResponseExtensions Response => response;
        /// <summary>BEP 43: true when the sender included <c>ro=1</c> in its query.</summary>
        public bool IsReadOnly { get; }

        public KrpcMessage (
            KrpcType messageType,
            QueryMethod queryMethod,
            ReadOnlyMemory<byte> tx,
            ReadOnlyMemory<byte> nodeId,
            KrpcRequestExtensions request,
            KrpcResponseExtensions response,
            bool isReadOnly = false)
        {
            MessageType = messageType;
            QueryMethod = queryMethod;
            transactionId = tx;
            this.nodeId = nodeId;
            this.request = request;
            this.response = response;
            IsReadOnly = isReadOnly;
        }

        public static KrpcMessage Parse (ReadOnlyMemory<byte> buffer)
        {
            var reader = new BEncodeReader (buffer.Span);

            reader.ExpectDictionaryBegin ();

            ReadOnlyMemory<byte> tx = default;
            ReadOnlyMemory<byte> y = default;
            ReadOnlyMemory<byte> q = default;

            ReadOnlyMemory<byte> nodeId = default;

            ReadOnlyMemory<byte> target = default;
            ReadOnlyMemory<byte> infoHash = default;
            ReadOnlyMemory<byte> token = default;

            ReadOnlyMemory<byte> nodes = default;
            ReadOnlyMemory<byte> nodes6 = default;
            ReadOnlyMemory<byte> values = default;

            int port = 0;
            bool impliedPort = false;
            bool isReadOnly = false;
            KrpcWant want = KrpcWant.None;

            while (reader.TryReadKey (out var key)) {
                if (key.SequenceEqual (Krpc.TransactionIdKey)) {
                    tx = reader.CaptureString (buffer);
                } else if (key.SequenceEqual (Krpc.MessageTypeKey)) {
                    y = reader.CaptureString (buffer);
                } else if (key.SequenceEqual (Krpc.QueryTypeKey)) {
                    q = reader.CaptureString (buffer);
                } else if (key.SequenceEqual (Krpc.ArgumentsKey)) {
                    reader.ExpectDictionaryBegin ();

                    while (reader.TryReadKey (out var akey)) {
                        if (akey.SequenceEqual (Krpc.NodeId))
                            nodeId = reader.CaptureString (buffer);

                        else if (akey.SequenceEqual (Krpc.Target))
                            target = reader.CaptureString (buffer);

                        else if (akey.SequenceEqual (Krpc.InfoHash))
                            infoHash = reader.CaptureString (buffer);

                        else if (akey.SequenceEqual (Krpc.Token))
                            token = reader.CaptureString (buffer);

                        else if (akey.SequenceEqual (Krpc.Port)) {
                            reader.CaptureInteger (buffer);
                            port = (int) reader.Integer;
                        } else if (akey.SequenceEqual (Krpc.ImpliedPort)) {
                            reader.CaptureInteger (buffer);
                            impliedPort = reader.Integer != 0;
                        } else if (akey.SequenceEqual (Krpc.Want))
                            want = ParseWant (reader.CaptureAny (buffer).Span);

                        else
                            reader.SkipValue ();
                    }
                } else if (key.SequenceEqual (Krpc.ReturnValuesKey)) {
                    reader.ExpectDictionaryBegin ();

                    while (reader.TryReadKey (out var rkey)) {
                        if (rkey.SequenceEqual (Krpc.NodeId))
                            nodeId = reader.CaptureString (buffer);

                        else if (rkey.SequenceEqual (Krpc.Nodes))
                            nodes = reader.CaptureString (buffer);

                        else if (rkey.SequenceEqual (Krpc.Nodes6))
                            nodes6 = reader.CaptureString (buffer);

                        else if (rkey.SequenceEqual (Krpc.Values))
                            values = reader.CaptureAny (buffer);

                        else if (rkey.SequenceEqual (Krpc.Token))
                            token = reader.CaptureString (buffer);

                        else
                            reader.SkipValue ();
                    }
                } else if (key.SequenceEqual (Krpc.ReadOnly)) {
                    // BEP 43: sender is a read-only node, do not add to routing table
                    reader.CaptureInteger (buffer);
                    isReadOnly = reader.Integer != 0;
                } else {
                    reader.SkipValue ();
                }
            }

            KrpcType messageType;
            QueryMethod queryMethod;

            if (y.Span.Length == 1 && y.Span[0] == (byte) 'q') {
                messageType = KrpcType.Query;
            } else if (y.Span.Length == 1 && y.Span[0] == (byte) 'r') {
                messageType = KrpcType.Response;
            } else if (y.Span.Length == 1 && y.Span[0] == (byte) 'e') {
                messageType = KrpcType.Error;
            } else {
                messageType = KrpcType.Unknown;
            }

            if (q.Span.SequenceEqual (Krpc.Queries.Ping))
                queryMethod = QueryMethod.Ping;
            else if (q.Span.SequenceEqual (Krpc.Queries.FindNode))
                queryMethod = QueryMethod.FindNode;
            else if (q.Span.SequenceEqual (Krpc.Queries.GetPeers))
                queryMethod = QueryMethod.GetPeers;
            else if (q.Span.SequenceEqual (Krpc.Queries.AnnouncePeer))
                queryMethod = QueryMethod.AnnouncePeer;
            else
                queryMethod = QueryMethod.Unknown;

            var request = new KrpcRequestExtensions (
                target,
                infoHash,
                token,
                port,
                impliedPort,
                want);

            var response = new KrpcResponseExtensions (
                nodes,
                values,
                token,
                nodes6);

            return new KrpcMessage (
                messageType,
                queryMethod,
                tx,
                nodeId,
                request,
                response,
                isReadOnly);
        }

        static KrpcWant ParseWant (ReadOnlySpan<byte> raw)
        {
            var want = KrpcWant.None;
            var reader = new BEncodeReader (raw);
            if (!reader.Read () || reader.Token != BEncodeToken.ListBegin)
                return want;

            while (reader.Read ()) {
                if (reader.Token == BEncodeToken.String) {
                    if (reader.Span.SequenceEqual (Krpc.WantNodes4))
                        want |= KrpcWant.Nodes;
                    else if (reader.Span.SequenceEqual (Krpc.WantNodes6))
                        want |= KrpcWant.Nodes6;
                } else if (reader.Token != BEncodeToken.Integer) {
                    break;
                }
            }
            return want;
        }
    }

    public static class KrpcMessageEncoder
    {
        // large enough fixed overhead for all static protocol bytes. Is it worth calculating this exactly?
        const int BaseSize = 96;

        public static int Estimate (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> query = default,
            ReadOnlySpan<byte> target = default,
            ReadOnlySpan<byte> infoHash = default,
            ReadOnlySpan<byte> token = default,
            ReadOnlySpan<byte> nodes = default,
            ReadOnlySpan<byte> values = default,
            bool readOnly = false,
            ReadOnlySpan<byte> nodes6 = default,
            KrpcWant want = KrpcWant.None)
        {
            return BaseSize
                 + transactionId.Length
                 + nodeId.Length
                 + query.Length
                 + target.Length
                 + infoHash.Length
                 + token.Length
                 + nodes.Length
                 + (nodes6.IsEmpty ? 0 : nodes6.Length + 16)
                 + values.Length
                 + (readOnly ? 8 : 0) // BEP 43: "2:ro" + "i1e" = 7 bytes
                 + (want == KrpcWant.None ? 0 : 16);
        }

        static void WriteWant (ref BEncodeWriter w, KrpcWant want)
        {
            if (want == KrpcWant.None)
                return;

            w.WriteString (Krpc.Want);
            w.BeginList ();
            if ((want & KrpcWant.Nodes) != 0)
                w.WriteString (Krpc.WantNodes4);
            if ((want & KrpcWant.Nodes6) != 0)
                w.WriteString (Krpc.WantNodes6);
            w.End ();
        }

        static void WriteNodes (ref BEncodeWriter w, ReadOnlySpan<byte> nodes, ReadOnlySpan<byte> nodes6)
        {
            if (!nodes.IsEmpty || nodes6.IsEmpty) {
                w.WriteString (Krpc.Nodes);
                w.WriteString (nodes);
            }
            if (!nodes6.IsEmpty) {
                w.WriteString (Krpc.Nodes6);
                w.WriteString (nodes6);
            }
        }

        //
        // --------------------------------------------------------------------
        // QUERY ENCODERS
        // --------------------------------------------------------------------
        //

        public static int EncodePing (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            bool readOnly = false)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ArgumentsKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            w.End ();

            w.WriteString (Krpc.QueryTypeKey);
            w.WriteString (Krpc.Queries.Ping);

            if (readOnly) {
                w.WriteString (Krpc.ReadOnly);
                w.WriteLong (1);
            }

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Query);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodePing (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            bool readOnly = false)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId, Krpc.Queries.Ping, readOnly: readOnly)];

            return buffer.Slice (0, EncodePing (buffer.Span, transactionId, nodeId, readOnly));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeFindNode (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> target,
            bool readOnly = false,
            KrpcWant want = KrpcWant.None)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ArgumentsKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            w.WriteString (Krpc.Target);
            w.WriteString (target);

            WriteWant (ref w, want);

            w.End ();

            w.WriteString (Krpc.QueryTypeKey);
            w.WriteString (Krpc.Queries.FindNode);

            if (readOnly) {
                w.WriteString (Krpc.ReadOnly);
                w.WriteLong (1);
            }

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Query);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeFindNode (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> target,
            bool readOnly = false,
            KrpcWant want = KrpcWant.None)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId,
                         Krpc.Queries.FindNode,
                         target: target,
                         readOnly: readOnly,
                         want: want)];

            return buffer.Slice (0, EncodeFindNode (buffer.Span, transactionId, nodeId, target, readOnly, want));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeGetPeers (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> infoHash,
            bool readOnly = false,
            KrpcWant want = KrpcWant.None)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ArgumentsKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            w.WriteString (Krpc.InfoHash);
            w.WriteString (infoHash);

            WriteWant (ref w, want);

            w.End ();

            w.WriteString (Krpc.QueryTypeKey);
            w.WriteString (Krpc.Queries.GetPeers);

            if (readOnly) {
                w.WriteString (Krpc.ReadOnly);
                w.WriteLong (1);
            }

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Query);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeGetPeers (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> infoHash,
            bool readOnly = false,
            KrpcWant want = KrpcWant.None)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId,
                         Krpc.Queries.GetPeers,
                         infoHash: infoHash,
                         readOnly: readOnly,
                         want: want)];

            return buffer.Slice (0, EncodeGetPeers (buffer.Span, transactionId, nodeId, infoHash, readOnly, want));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeAnnouncePeer (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> infoHash,
            ReadOnlySpan<byte> token,
            int port,
            bool impliedPort,
            bool readOnly = false)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ArgumentsKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            if (impliedPort) {
                w.WriteString (Krpc.ImpliedPort);
                w.WriteLong (1);
            }

            w.WriteString (Krpc.InfoHash);
            w.WriteString (infoHash);

            w.WriteString (Krpc.Port);
            w.WriteLong (port);

            w.WriteString (Krpc.Token);
            w.WriteString (token);

            w.End ();

            w.WriteString (Krpc.QueryTypeKey);
            w.WriteString (Krpc.Queries.AnnouncePeer);

            if (readOnly) {
                w.WriteString (Krpc.ReadOnly);
                w.WriteLong (1);
            }

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Query);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeAnnouncePeer (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> infoHash,
            ReadOnlySpan<byte> token,
            int port,
            bool impliedPort,
            bool readOnly = false)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId,
                         Krpc.Queries.AnnouncePeer,
                         infoHash: infoHash,
                         token: token,
                         readOnly: readOnly)];

            return buffer.Slice (0, EncodeAnnouncePeer (
                buffer.Span,
                transactionId,
                nodeId,
                infoHash,
                token,
                port,
                impliedPort,
                readOnly));
        }

        //
        // --------------------------------------------------------------------
        // RESPONSE ENCODERS
        // --------------------------------------------------------------------
        //

        public static int EncodePingResponse (
        Span<byte> dest,
        ReadOnlySpan<byte> transactionId,
        ReadOnlySpan<byte> nodeId)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ReturnValuesKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            w.End ();

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Response);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodePingResponse (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId)];

            return buffer.Slice (0, EncodePingResponse (buffer.Span, transactionId, nodeId));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeFindNodeResponse (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> nodes,
            ReadOnlySpan<byte> nodes6 = default)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ReturnValuesKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            WriteNodes (ref w, nodes, nodes6);

            w.End ();

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Response);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeFindNodeResponse (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> nodes,
            ReadOnlySpan<byte> nodes6 = default)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId, nodes: nodes, nodes6: nodes6)];

            return buffer.Slice (0, EncodeFindNodeResponse (buffer.Span, transactionId, nodeId, nodes, nodes6));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeGetPeersResponseNodes (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> token,
            ReadOnlySpan<byte> nodes,
            ReadOnlySpan<byte> nodes6 = default)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ReturnValuesKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            WriteNodes (ref w, nodes, nodes6);

            w.WriteString (Krpc.Token);
            w.WriteString (token);

            w.End ();

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Response);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeGetPeersResponseNodes (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> token,
            ReadOnlySpan<byte> nodes,
            ReadOnlySpan<byte> nodes6 = default)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId,
                         token: token,
                         nodes: nodes,
                         nodes6: nodes6)];

            return buffer.Slice (0, EncodeGetPeersResponseNodes (
                buffer.Span,
                transactionId,
                nodeId,
                token,
                nodes,
                nodes6));
        }

        //
        // --------------------------------------------------------------------
        //

        public static int EncodeGetPeersResponseValues (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> token,
            ReadOnlySpan<byte> values)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            w.WriteString (Krpc.ReturnValuesKey);
            w.BeginDict ();

            w.WriteString (Krpc.NodeId);
            w.WriteString (nodeId);

            w.WriteString (Krpc.Token);
            w.WriteString (token);

            w.WriteString (Krpc.Values);
            w.WriteRaw (values);

            w.End ();

            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Response);

            w.End ();

            return w.Written;
        }

        public static ReadOnlyMemory<byte> EncodeGetPeersResponseValues (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId,
            ReadOnlySpan<byte> token,
            ReadOnlySpan<byte> values)
        {
            Memory<byte> buffer = new byte[
                Estimate (transactionId, nodeId,
                         token: token,
                         values: values)];

            return buffer.Slice (0, EncodeGetPeersResponseValues (
                buffer.Span,
                transactionId,
                nodeId,
                token,
                values));
        }

        public static int EncodeAnnouncePeerResponse (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId)
        {
            return EncodePingResponse (
                dest,
                transactionId,
                nodeId);
        }

        public static ReadOnlyMemory<byte> EncodeAnnouncePeerResponse (
            ReadOnlySpan<byte> transactionId,
            ReadOnlySpan<byte> nodeId)
        {
            return EncodePingResponse (
                transactionId,
                nodeId);
        }

        public static int EncodeError (
            Span<byte> dest,
            ReadOnlySpan<byte> transactionId,
            int errorCode,
            ReadOnlySpan<byte> errorMessage)
        {
            var w = new BEncodeWriter (dest);

            w.BeginDict ();

            // t -> transaction id
            w.WriteString (Krpc.TransactionIdKey);
            w.WriteString (transactionId);

            // y -> error
            w.WriteString (Krpc.MessageTypeKey);
            w.WriteString (Krpc.MessageType.Error); // "e"

            // e -> list [code, message]
            w.WriteString ("e"u8);
            w.BeginList (); // <-- you need this (see note below)

            w.WriteLong (errorCode);
            w.WriteString (errorMessage);

            w.End (); // list

            w.End (); // dict

            return w.Written;
        }

        public static byte[] EncodeError (
            ReadOnlySpan<byte> transactionId,
            int errorCode,
            ReadOnlySpan<byte> errorMessage)
        {
            const int BaseSize = 128;

            var buffer = new byte[
                BaseSize +
                transactionId.Length +
                errorMessage.Length];

            EncodeError (buffer, transactionId, errorCode, errorMessage);
            return buffer;
        }
    }
}
