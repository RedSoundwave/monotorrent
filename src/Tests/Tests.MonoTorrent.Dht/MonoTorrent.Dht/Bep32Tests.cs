using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using MonoTorrent.BEncoding;
using MonoTorrent.Connections;
using MonoTorrent.Connections.Dht;
using MonoTorrent.Dht.Messages;
using MonoTorrent.Dht.Tasks;

using NUnit.Framework;

using ReusableTasks;

namespace MonoTorrent.Dht
{
    [TestFixture]
    public class Bep32Tests
    {
        readonly NodeId id = new NodeId (Encoding.UTF8.GetBytes ("abcdefghij0123456789").AsSpan ());
        static ReadOnlySpan<byte> Tx => "aa"u8;

        static Node NodeAt (string address, int port)
            => new Node (NodeId.Create (), new CompactEndPoint (IPAddress.Parse (address), port));

        static ReadOnlyMemory<byte> Ping (NodeId sender)
        {
            var tx = TransactionId.NextId ();
            return KrpcMessageEncoder.EncodePing (tx, sender.Span);
        }

        static ReadOnlyMemory<byte> FindNode (NodeId sender, KrpcWant want)
        {
            var tx = TransactionId.NextId ();
            return KrpcMessageEncoder.EncodeFindNode (tx, sender.Span, NodeId.Create ().Span, want: want);
        }

        static ReadOnlyMemory<byte> GetPeers (NodeId sender, KrpcWant want)
        {
            var tx = TransactionId.NextId ();
            return KrpcMessageEncoder.EncodeGetPeers (tx, sender.Span, NodeId.Create ().Span, want: want);
        }

        static string Text (ReadOnlyMemory<byte> data)
            => Encoding.Latin1.GetString (data.Span);

        [Test]
        public void CompactIPv6Node_RoundTrips ()
        {
            var node = NodeAt ("2001:db8::17", 6881);
            var compact = node.CompactNode ();
            Assert.AreEqual (38, compact.Span.Length);

            var decoded = Node.FromCompactNode (compact.Span, AddressFamily.InterNetworkV6);
            Assert.IsNotNull (decoded);
            Assert.AreEqual (node.Id, decoded.Id);
            Assert.AreEqual (node.EndPoint, decoded.EndPoint);
            Assert.AreEqual (AddressFamily.InterNetworkV6, decoded.EndPoint.Family);
            Assert.AreEqual (6881, decoded.EndPoint.Port);
        }

        [Test]
        public void CompactIPv6Node_RejectsObviouslyBadAddresses ()
        {
            foreach (var address in new[] { "::", "::1", "fe80::1", "ff02::1", "::ffff:1.2.3.4" }) {
                var compact = NodeAt (address, 6881).CompactNode ();
                Assert.IsNull (Node.FromCompactNode (compact.Span, AddressFamily.InterNetworkV6), address);
            }
        }

        [Test]
        public void FindNodeResponse_Nodes6_EncodeDecode ()
        {
            var nodes6 = Node.CompactNode (new List<Node> { NodeAt ("2001:db8::1", 1000), NodeAt ("2001:db8::2", 2000) });
            var message = KrpcMessageEncoder.EncodeFindNodeResponse (Tx, id.Span, default, nodes6.Span);

            StringAssert.Contains ("6:nodes676:", Text (message));
            StringAssert.DoesNotContain ("5:nodes", Text (message));

            var parsed = KrpcMessage.Parse (message);
            Assert.IsFalse (parsed.Response.HasNodes);
            Assert.IsTrue (parsed.Response.HasNodes6);
            Assert.IsTrue (parsed.Response.Nodes6.SequenceEqual (nodes6.Span));

            var decoded = Node.FromCompactNodes (parsed.Response.Nodes6, AddressFamily.InterNetworkV6).ToArray ();
            Assert.AreEqual (2, decoded.Length);
            Assert.AreEqual (new CompactEndPoint (IPAddress.Parse ("2001:db8::2"), 2000), decoded[1].EndPoint);
        }

        [Test]
        public void GetPeersResponse_NodesAndNodes6_EncodeDecode ()
        {
            var nodes = Node.CompactNode (new List<Node> { NodeAt ("1.2.3.4", 1000) });
            var nodes6 = Node.CompactNode (new List<Node> { NodeAt ("2001:db8::1", 1000) });
            var token = (BEncodedString) "token";
            var message = KrpcMessageEncoder.EncodeGetPeersResponseNodes (Tx, id.Span, token.Span, nodes.Span, nodes6.Span);

            var text = Text (message);
            Assert.Less (text.IndexOf ("5:nodes26:"), text.IndexOf ("6:nodes638:"));

            var parsed = KrpcMessage.Parse (message);
            Assert.IsTrue (parsed.Response.Nodes.SequenceEqual (nodes.Span));
            Assert.IsTrue (parsed.Response.Nodes6.SequenceEqual (nodes6.Span));
            Assert.IsTrue (parsed.Response.Token.SequenceEqual (token.Span));
        }

        [Test]
        public void FindNodeResponse_WithoutNodes6_IsUnchanged ()
        {
            var message = KrpcMessageEncoder.EncodeFindNodeResponse (Tx, id.Span, "def456..."u8);
            Assert.AreEqual ("d1:rd2:id20:abcdefghij01234567895:nodes9:def456...e1:t2:aa1:y1:re", Text (message));
        }

        [Test]
        public void IPv6Values_EncodeDecode ()
        {
            var peer = NodeAt ("2001:db8::99", 51413);
            var compact = peer.CompactEndPoint ();
            Assert.AreEqual (18, compact.Span.Length);

            var values = new BEncodedList { compact }.Encode ();
            var message = KrpcMessageEncoder.EncodeGetPeersResponseValues (Tx, id.Span, "tok"u8, values);
            var parsed = KrpcMessage.Parse (message);

            var reader = new BEncodeReader (parsed.Response.Values);
            Assert.IsTrue (reader.Read ());
            Assert.AreEqual (BEncodeToken.ListBegin, reader.Token);
            Assert.IsTrue (reader.Read ());
            var peers = PeerInfo.FromCompact (reader.Span, AddressFamily.InterNetworkV6);
            Assert.AreEqual (1, peers.Count);
            Assert.AreEqual (new Uri ("ipv6://[2001:db8::99]:51413"), peers[0].ConnectionUri);
        }

        [Test]
        public void Want_EncodeDecode ()
        {
            var both = KrpcMessageEncoder.EncodeFindNode (Tx, id.Span, id.Span, want: KrpcWant.Nodes | KrpcWant.Nodes6);
            StringAssert.Contains ("4:wantl2:n42:n6e", Text (both));
            Assert.AreEqual (KrpcWant.Nodes | KrpcWant.Nodes6, KrpcMessage.Parse (both).Request.Want);

            var n6 = KrpcMessageEncoder.EncodeGetPeers (Tx, id.Span, id.Span, want: KrpcWant.Nodes6);
            StringAssert.Contains ("4:wantl2:n6e", Text (n6));
            Assert.AreEqual (KrpcWant.Nodes6, KrpcMessage.Parse (n6).Request.Want);

            var none = KrpcMessageEncoder.EncodeGetPeers (Tx, id.Span, id.Span);
            StringAssert.DoesNotContain ("want", Text (none));
            Assert.AreEqual (KrpcWant.None, KrpcMessage.Parse (none).Request.Want);
        }

        [Test]
        public void Want_UnknownEntriesAreIgnored ()
        {
            var raw = Encoding.Latin1.GetBytes ("d1:ad2:id20:abcdefghij01234567896:target20:abcdefghij01234567894:wantl2:n52:n6i3eee1:q9:find_node1:t2:aa1:y1:qe");
            var parsed = KrpcMessage.Parse (raw);
            Assert.AreEqual (QueryMethod.FindNode, parsed.QueryMethod);
            Assert.AreEqual (KrpcWant.Nodes6, parsed.Request.Want);
        }

        [Test]
        public void NodesFrom_UsesOwnFamilyOnly ()
        {
            var nodes = Node.CompactNode (new List<Node> { NodeAt ("1.2.3.4", 1000) });
            var nodes6 = Node.CompactNode (new List<Node> { NodeAt ("2001:db8::1", 1000) });
            var message = KrpcMessageEncoder.EncodeFindNodeResponse (Tx, id.Span, nodes.Span, nodes6.Span);
            var parsed = KrpcMessage.Parse (message);

            using var v4 = new DhtEngine ();
            using var v6 = new DhtEngine (AddressFamily.InterNetworkV6);

            var fromV4 = v4.NodesFrom (parsed.Response).Single ();
            var fromV6 = v6.NodesFrom (parsed.Response).Single ();
            Assert.AreEqual (AddressFamily.InterNetwork, fromV4.EndPoint.Family);
            Assert.AreEqual (AddressFamily.InterNetworkV6, fromV6.EndPoint.Family);
        }

        [Test]
        public void Engine_DefaultsToIPv4 ()
        {
            using var engine = new DhtEngine ();
            Assert.AreEqual (AddressFamily.InterNetwork, engine.AddressFamily);
            Assert.AreEqual (DhtEngine.DefaultBootstrapRouters, engine.BootstrapRouters);
        }

        [Test]
        public void Engine_IPv6UsesIPv6Routers ()
        {
            using var engine = new DhtEngine (AddressFamily.InterNetworkV6);
            Assert.AreEqual (AddressFamily.InterNetworkV6, engine.AddressFamily);
            Assert.AreEqual (DhtEngine.DefaultBootstrapRoutersV6, engine.BootstrapRouters);
            Assert.Throws<ArgumentException> (() => new DhtEngine (AddressFamily.Unix));
        }

        [Test]
        public async Task IPv6Engine_IgnoresIPv4Nodes ()
            => await IgnoresOtherFamily (AddressFamily.InterNetworkV6, NodeAt ("1.2.3.4", 1000), NodeAt ("2001:db8::5", 1000));

        [Test]
        public async Task IPv4Engine_IgnoresIPv6Nodes ()
            => await IgnoresOtherFamily (AddressFamily.InterNetwork, NodeAt ("2001:db8::5", 1000), NodeAt ("1.2.3.4", 1000));

        async Task IgnoresOtherFamily (AddressFamily family, Node foreign, Node native)
        {
            using var engine = new DhtEngine (family);
            var listener = new TestListener ();
            await engine.SetListenerAsync (listener);

            var replies = new List<CompactEndPoint> ();
            var nativeReplied = new TaskCompletionSource<object> ();
            listener.MessageSent += (data, endpoint) => {
                lock (replies)
                    replies.Add (endpoint);
                if (endpoint.Equals (native.EndPoint))
                    nativeReplied.TrySetResult (null);
            };

            listener.RaiseMessageReceived (Ping (foreign.Id), foreign.EndPoint);
            listener.RaiseMessageReceived (Ping (native.Id), native.EndPoint);
            await nativeReplied.Task.WithTimeout (5000);

            await DhtEngine.MainLoop;
            Assert.IsNull (engine.RoutingTable.FindNode (foreign.Id));
            Assert.IsNotNull (engine.RoutingTable.FindNode (native.Id));
            lock (replies)
                CollectionAssert.DoesNotContain (replies, foreign.EndPoint);

            engine.Add (foreign);
            Assert.AreEqual (0, engine.MessageLoop.DhtMessageFactory.RegisteredMessages);

            var saved = await engine.SaveNodesAsync ();
            Assert.AreEqual (Node.CompactNodeSize (family), saved.Length);
        }

        [TestCase (AddressFamily.InterNetworkV6, KrpcWant.None, false, true)]
        [TestCase (AddressFamily.InterNetworkV6, KrpcWant.Nodes6, false, true)]
        [TestCase (AddressFamily.InterNetworkV6, KrpcWant.Nodes | KrpcWant.Nodes6, false, true)]
        [TestCase (AddressFamily.InterNetworkV6, KrpcWant.Nodes, false, false)]
        [TestCase (AddressFamily.InterNetwork, KrpcWant.None, true, false)]
        [TestCase (AddressFamily.InterNetwork, KrpcWant.Nodes, true, false)]
        [TestCase (AddressFamily.InterNetwork, KrpcWant.Nodes | KrpcWant.Nodes6, true, false)]
        [TestCase (AddressFamily.InterNetwork, KrpcWant.Nodes6, false, false)]
        public async Task FindNodeAndGetPeers_HonourWant (AddressFamily family, KrpcWant want, bool expectNodes, bool expectNodes6)
        {
            using var engine = new DhtEngine (family);
            var listener = new TestListener ();
            await engine.SetListenerAsync (listener);

            Node Make (string v4, string v6, int port)
                => family == AddressFamily.InterNetwork ? NodeAt (v4, port) : NodeAt (v6, port);

            var known = Make ("2.2.2.2", "2001:db8::2", 2000);
            var requester = Make ("3.3.3.3", "2001:db8::3", 3000);
            await DhtEngine.MainLoop;
            engine.RoutingTable.Add (known);

            var responses = new List<ReadOnlyMemory<byte>> ();
            var done = new TaskCompletionSource<object> ();
            listener.MessageSent += (data, endpoint) => {
                if (!endpoint.Equals (requester.EndPoint))
                    return;
                lock (responses) {
                    responses.Add (data.ToArray ());
                    if (responses.Count == 2)
                        done.TrySetResult (null);
                }
            };

            listener.RaiseMessageReceived (FindNode (requester.Id, want), requester.EndPoint);
            listener.RaiseMessageReceived (GetPeers (requester.Id, want), requester.EndPoint);
            await done.Task.WithTimeout (5000);

            foreach (var raw in responses) {
                var response = KrpcMessage.Parse (raw);
                Assert.AreEqual (KrpcType.Response, response.MessageType);
                Assert.AreEqual (expectNodes, response.Response.HasNodes, "nodes");
                Assert.AreEqual (expectNodes6, response.Response.HasNodes6, "nodes6");
                if (expectNodes6)
                    Assert.IsTrue (Node.FromCompactNodes (response.Response.Nodes6, AddressFamily.InterNetworkV6).Any (n => n.Id.Equals (known.Id)));
                if (expectNodes)
                    Assert.IsTrue (Node.FromCompactNodes (response.Response.Nodes, AddressFamily.InterNetwork).Any (n => n.Id.Equals (known.Id)));
            }
        }

        [Test]
        public async Task GetPeers_ParsesMixedFamilyValues ()
        {
            using var engine = new DhtEngine (AddressFamily.InterNetworkV6);
            var listener = new TestListener ();
            await engine.SetListenerAsync (listener);

            var remote = NodeAt ("2001:db8::7", 7000);
            await DhtEngine.MainLoop;
            engine.RoutingTable.Add (remote);

            var values = new BEncodedList {
                NodeAt ("2001:db8::99", 51413).CompactEndPoint (),
                NodeAt ("5.6.7.8", 6881).CompactEndPoint ()
            }.Encode ();

            listener.MessageSent += (data, endpoint) => {
                var message = KrpcMessage.Parse (data);
                if (message.QueryMethod == QueryMethod.GetPeers && endpoint.Equals (remote.EndPoint)) {
                    var response = KrpcMessageEncoder.EncodeGetPeersResponseValues (message.TransactionId, remote.Id.Span, "tok"u8, values);
                    listener.RaiseMessageReceived (response, endpoint);
                }
            };

            var found = new TaskCompletionSource<IList<PeerInfo>> ();
            engine.PeersFound += (o, e) => found.TrySetResult (e.Peers);

            var infoHash = new InfoHash (new byte[20]);
            await DhtEngine.MainLoop;
            var task = new GetPeersTask (engine, infoHash).ExecuteAsync ();
            var peers = await found.Task.WithTimeout (5000);
            await task.WithTimeout (5000);

            Assert.AreEqual (2, peers.Count);
            Assert.AreEqual (new Uri ("ipv6://[2001:db8::99]:51413"), peers[0].ConnectionUri);
            Assert.AreEqual (new Uri ("ipv4://5.6.7.8:6881"), peers[1].ConnectionUri);
        }

        [Test]
        public async Task TwoIPv6Engines_FindEachOtherAndAPeer ()
        {
            var network = new InMemoryDhtNetwork ();
            var endpointA = new CompactEndPoint (IPAddress.Parse ("2001:db8::a"), 1000);
            var endpointB = new CompactEndPoint (IPAddress.Parse ("2001:db8::b"), 2000);

            using var a = new DhtEngine (AddressFamily.InterNetworkV6);
            using var b = new DhtEngine (AddressFamily.InterNetworkV6);
            foreach (var engine in new[] { a, b })
                await engine.SetBootstrapRoutersAsync (Array.Empty<BootstrapRouter> ());

            await a.SetListenerAsync (network.Create (endpointA));
            await b.SetListenerAsync (network.Create (endpointB));

            await b.StartAsync ();
            await WaitUntilInitialised (b);

            var compactB = new Node (b.RoutingTable.LocalNodeId, endpointB).CompactNode ();
            await a.StartAsync (compactB.AsMemory ());
            await WaitUntilInitialised (a);

            await DhtEngine.MainLoop;
            Assert.IsNotNull (a.RoutingTable.FindNode (b.RoutingTable.LocalNodeId), "a knows b");
            Assert.IsNotNull (b.RoutingTable.FindNode (a.RoutingTable.LocalNodeId), "b knows a");

            var infoHash = new InfoHash (Enumerable.Repeat ((byte) 7, 20).ToArray ());
            await DhtEngine.MainLoop;
            await new AnnounceTask (a, infoHash, 6881).ExecuteAsync ().WithTimeout (10000);

            var found = new TaskCompletionSource<IList<PeerInfo>> ();
            b.PeersFound += (o, e) => found.TrySetResult (e.Peers);

            await DhtEngine.MainLoop;
            Assert.IsTrue (b.Torrents.ContainsKey (new NodeId (infoHash.Span)), "b stored the announce");

            var endpointC = new CompactEndPoint (IPAddress.Parse ("2001:db8::c"), 3000);
            using var c = new DhtEngine (AddressFamily.InterNetworkV6);
            await c.SetBootstrapRoutersAsync (Array.Empty<BootstrapRouter> ());
            await c.SetListenerAsync (network.Create (endpointC));
            var foundByC = new TaskCompletionSource<IList<PeerInfo>> ();
            c.PeersFound += (o, e) => foundByC.TrySetResult (e.Peers);

            await c.StartAsync (compactB.AsMemory ());
            await WaitUntilInitialised (c);

            await DhtEngine.MainLoop;
            var getPeers = new GetPeersTask (c, infoHash).ExecuteAsync ();
            var peers = await foundByC.Task.WithTimeout (10000);
            await getPeers.WithTimeout (10000);

            Assert.AreEqual (1, peers.Count);
            Assert.AreEqual (new Uri ("ipv6://[2001:db8::a]:1000"), peers[0].ConnectionUri);
        }

        static async Task WaitUntilInitialised (DhtEngine engine)
        {
            for (int i = 0; i < 500 && engine.State == DhtState.Initialising; i++)
                await Task.Delay (20);
            Assert.AreNotEqual (DhtState.Initialising, engine.State);
        }

        class InMemoryDhtNetwork
        {
            readonly Dictionary<CompactEndPoint, InMemoryDhtListener> listeners = new Dictionary<CompactEndPoint, InMemoryDhtListener> ();

            public InMemoryDhtListener Create (CompactEndPoint endPoint)
            {
                var listener = new InMemoryDhtListener (this, endPoint);
                lock (listeners)
                    listeners[endPoint] = listener;
                return listener;
            }

            public void Deliver (ReadOnlyMemory<byte> buffer, CompactEndPoint from, CompactEndPoint to)
            {
                InMemoryDhtListener target;
                lock (listeners)
                    if (!listeners.TryGetValue (to, out target))
                        return;

                var copy = buffer.ToArray ();
                Task.Run (() => target.Receive (copy, from));
            }
        }

        class InMemoryDhtListener : IDhtListener
        {
            readonly InMemoryDhtNetwork network;
            readonly CompactEndPoint endPoint;

            public event Action<ReadOnlyMemory<byte>, CompactEndPoint> MessageReceived;
            public event EventHandler<EventArgs> StatusChanged;

            public IPEndPoint LocalEndPoint { get; }
            public ListenerStatus Status { get; private set; } = ListenerStatus.NotListening;

            public InMemoryDhtListener (InMemoryDhtNetwork network, CompactEndPoint endPoint)
            {
                this.network = network;
                this.endPoint = endPoint;
                LocalEndPoint = new IPEndPoint (new IPAddress (endPoint.Address), endPoint.Port);
            }

            public void Receive (ReadOnlyMemory<byte> buffer, CompactEndPoint from)
            {
                if (Status == ListenerStatus.Listening)
                    MessageReceived?.Invoke (buffer, from);
            }

            public ReusableTask SendAsync (ReadOnlyMemory<byte> buffer, CompactEndPoint destination)
            {
                network.Deliver (buffer, endPoint, destination);
                return ReusableTask.CompletedTask;
            }

            public void Start ()
                => SetStatus (ListenerStatus.Listening);

            public void Stop ()
                => SetStatus (ListenerStatus.NotListening);

            void SetStatus (ListenerStatus status)
            {
                Status = status;
                StatusChanged?.Invoke (this, EventArgs.Empty);
            }
        }
    }
}
