using System;
using System.Net;

using MonoTorrent.Connections.Peer.Encryption;
using MonoTorrent.Messages;
using MonoTorrent.Messages.Peer.Libtorrent;

using NUnit.Framework;

namespace MonoTorrent.Client
{
    [TestFixture]
    public class HolePunchManagerTests
    {
        ConnectionPair conn;
        TorrentManager Manager { get; set; }

        [SetUp]
        public void Setup ()
        {
            conn = new ConnectionPair ().DisposeAfterTimeout ();
            Manager = TestRig.CreateMultiFileManager (new long[] { Constants.BlockSize * 4 }, Constants.BlockSize);
        }

        [TearDown]
        public void Teardown ()
            => conn.Dispose ();

        PeerId Connected (string uri, bool supportsHolePunch = true)
        {
            var id = new PeerId (new Peer (new PeerInfo (new Uri (uri))), conn.Outgoing, new BitField (Manager.Torrent.PieceCount ()), Manager.InfoHashes.V1OrV2, PlainTextEncryption.Instance, PlainTextEncryption.Instance, Software.Synthetic);
            id.SupportsLTMessages = true;
            id.MessageQueue.SetReady ();
            id.ExtensionSupports = supportsHolePunch
                ? new ExtensionSupports (new[] { MessageEncoder.Extended.PeerExchangeSupport, MessageEncoder.Extended.HolePunchSupport })
                : new ExtensionSupports (new[] { MessageEncoder.Extended.PeerExchangeSupport });
            Manager.Peers.ConnectedPeers.Add (id);
            return id;
        }

        static Memory<byte> Write (Extended.HolePunchMessageType type, IPEndPoint endPoint, Extended.HolePunchError error)
        {
            var supports = new ExtensionSupports (new[] { MessageEncoder.Extended.HolePunchSupport });
            var (message, _) = MessageEncoder.Extended.WriteHolePunch (supports, type, endPoint, error);
            return message;
        }

        static void AssertMessage (PeerId peer, Extended.HolePunchMessageType type, IPEndPoint endPoint, Extended.HolePunchError error = Extended.HolePunchError.None)
        {
            var message = new Extended.HolePunchMessage (peer.MessageQueue.TryDequeue ());
            Assert.IsTrue (message.IsValid);
            Assert.AreEqual (type, message.MessageType);
            Assert.AreEqual (endPoint, message.EndPoint);
            Assert.AreEqual (error, message.Error);
        }

        [Test]
        public void Message_RoundTrips_IPv4 ()
        {
            var endPoint = new IPEndPoint (IPAddress.Parse ("10.1.2.3"), 51413);
            var message = new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Error, endPoint, Extended.HolePunchError.NotConnected));

            Assert.IsTrue (message.IsValid);
            Assert.AreEqual (Extended.HolePunchMessageType.Error, message.MessageType);
            Assert.AreEqual (endPoint, message.EndPoint);
            Assert.AreEqual (Extended.HolePunchError.NotConnected, message.Error);
        }

        [Test]
        public void Message_RoundTrips_IPv6 ()
        {
            var endPoint = new IPEndPoint (IPAddress.Parse ("2001:db8::7"), 6881);
            var message = new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Connect, endPoint, Extended.HolePunchError.None));

            Assert.AreEqual (Extended.HolePunchMessageType.Connect, message.MessageType);
            Assert.AreEqual (endPoint, message.EndPoint);
        }

        [Test]
        public void Message_WithUnknownAddressType_IsInvalid ()
        {
            var raw = Write (Extended.HolePunchMessageType.Connect, new IPEndPoint (IPAddress.Loopback, 1), Extended.HolePunchError.None);
            raw.Span[7] = 9;

            Assert.IsFalse (new Extended.HolePunchMessage (raw).IsValid);
        }

        [Test]
        public void Rendezvous_ConnectsBothSides ()
        {
            var initiator = Connected ("ipv4://1.1.1.1:1111");
            var target = Connected ("ipv4://2.2.2.2:2222");

            Manager.HolePunch.Handle (initiator, new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Rendezvous, new IPEndPoint (IPAddress.Parse ("2.2.2.2"), 2222), Extended.HolePunchError.None)));

            AssertMessage (target, Extended.HolePunchMessageType.Connect, new IPEndPoint (IPAddress.Parse ("1.1.1.1"), 1111));
            AssertMessage (initiator, Extended.HolePunchMessageType.Connect, new IPEndPoint (IPAddress.Parse ("2.2.2.2"), 2222));
        }

        [Test]
        public void Rendezvous_UsesAdvertisedListenPort ()
        {
            var initiator = Connected ("ipv4://1.1.1.1:50000");
            initiator.Peer.LocalPort = 6881;
            var target = Connected ("ipv4://2.2.2.2:2222");

            Manager.HolePunch.Handle (initiator, new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Rendezvous, new IPEndPoint (IPAddress.Parse ("2.2.2.2"), 2222), Extended.HolePunchError.None)));

            AssertMessage (target, Extended.HolePunchMessageType.Connect, new IPEndPoint (IPAddress.Parse ("1.1.1.1"), 6881));
        }

        [Test]
        public void Rendezvous_TargetNotConnected_ReturnsError ()
        {
            var initiator = Connected ("ipv4://1.1.1.1:1111");
            var target = new IPEndPoint (IPAddress.Parse ("3.3.3.3"), 3333);

            Manager.HolePunch.Handle (initiator, new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Rendezvous, target, Extended.HolePunchError.None)));

            AssertMessage (initiator, Extended.HolePunchMessageType.Error, target, Extended.HolePunchError.NotConnected);
        }

        [Test]
        public void Rendezvous_TargetWithoutSupport_ReturnsError ()
        {
            var initiator = Connected ("ipv4://1.1.1.1:1111");
            Connected ("ipv4://2.2.2.2:2222", supportsHolePunch: false);
            var target = new IPEndPoint (IPAddress.Parse ("2.2.2.2"), 2222);

            Manager.HolePunch.Handle (initiator, new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Rendezvous, target, Extended.HolePunchError.None)));

            AssertMessage (initiator, Extended.HolePunchMessageType.Error, target, Extended.HolePunchError.NoSupport);
        }

        [Test]
        public void Rendezvous_ToSelf_ReturnsError ()
        {
            var initiator = Connected ("ipv4://1.1.1.1:1111");
            var target = new IPEndPoint (IPAddress.Parse ("1.1.1.1"), 1111);

            Manager.HolePunch.Handle (initiator, new Extended.HolePunchMessage (Write (Extended.HolePunchMessageType.Rendezvous, target, Extended.HolePunchError.None)));

            AssertMessage (initiator, Extended.HolePunchMessageType.Error, target, Extended.HolePunchError.NoSelf);
        }

        [Test]
        public void FailedPexPeer_AsksItsSourceOnce ()
        {
            var relay = Connected ("ipv4://1.1.1.1:1111");
            var unreachable = new PeerInfo (new Uri ("ipv4://4.4.4.4:4444"));
            Manager.HolePunch.RecordSource (new[] { unreachable }, relay);

            Assert.IsTrue (Manager.HolePunch.TryRequestRendezvous (new Peer (unreachable)));
            Assert.IsFalse (Manager.HolePunch.TryRequestRendezvous (new Peer (unreachable)));
            AssertMessage (relay, Extended.HolePunchMessageType.Rendezvous, new IPEndPoint (IPAddress.Parse ("4.4.4.4"), 4444));
        }

        [Test]
        public void PeerWithoutPexSource_IsNotRequested ()
            => Assert.IsFalse (Manager.HolePunch.TryRequestRendezvous (new Peer (new PeerInfo (new Uri ("ipv4://5.5.5.5:5555")))));
    }
}
