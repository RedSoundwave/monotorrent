using System;
using System.Collections.Generic;
using System.Text;

using NUnit.Framework;

namespace MonoTorrent.Connections.Utp
{
    [TestFixture]
    public class UtpPacketCodecTests
    {
        [Test]
        public void RoundTripWithoutExtensions ()
        {
            var header = new UtpHeader (UtpPacketType.Data, 0xBEEF, 0x01020304, 0xA0B0C0D0, 1048576, 0xFFFF, 0x1234);
            var payload = new byte[] { 1, 2, 3, 4, 5 };
            var buffer = new byte[UtpPacketCodec.GetLength (0, payload.Length)];

            int length = UtpPacketCodec.Write (buffer, header, default, payload);

            Assert.AreEqual (25, length);
            Assert.AreEqual (0x01, buffer[0]);
            Assert.AreEqual (0, buffer[1]);
            Assert.AreEqual (new byte[] { 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 }, buffer.AsSpan (2, 6).ToArray ());
            Assert.IsTrue (UtpPacketCodec.TryRead (buffer, out var decoded, out var sack, out var decodedPayload));
            Assert.AreEqual (header, decoded);
            Assert.IsTrue (sack.IsEmpty);
            Assert.AreEqual (payload, decodedPayload.ToArray ());
        }

        [Test]
        public void RoundTripWithSelectiveAck ()
        {
            var received = new Dictionary<ushort, bool> { { 0xFFFF, true }, { 2, true }, { 40, true } };
            Span<byte> mask = stackalloc byte[SelectiveAck.MaxLength];
            int maskLength = SelectiveAck.Build ((ushort) 0xFFFC, received, mask);
            var header = new UtpHeader (UtpPacketType.State, 7, 1, 2, 3, 100, 0xFFFC);
            var buffer = new byte[UtpPacketCodec.GetLength (maskLength, 0)];

            int length = UtpPacketCodec.Write (buffer, header, mask.Slice (0, maskLength), default);

            Assert.AreEqual (8, maskLength);
            Assert.AreEqual (30, length);
            Assert.AreEqual (0x21, buffer[0]);
            Assert.AreEqual (UtpPacketCodec.SelectiveAckExtension, buffer[1]);
            Assert.IsTrue (UtpPacketCodec.TryRead (buffer, out var decoded, out var sack, out var payload));
            Assert.AreEqual (header, decoded);
            Assert.IsTrue (payload.IsEmpty);
            Assert.AreEqual (8, sack.Length);

            var acked = new List<ushort> ();
            for (int bit = 0; bit < sack.Length * 8; bit++)
                if (SelectiveAck.IsSet (sack, bit))
                    acked.Add (SelectiveAck.GetSequenceNumber (decoded.AckNumber, bit));
            CollectionAssert.AreEqual (new ushort[] { 0xFFFF, 2, 40 }, acked);
        }

        [Test]
        public void SelectiveAckBitOrder ()
        {
            var received = new Dictionary<ushort, bool> { { 12, true }, { 21, true } };
            var mask = new byte[SelectiveAck.MaxLength];

            int length = SelectiveAck.Build (10, received, mask);

            Assert.AreEqual (4, length);
            Assert.AreEqual (0b0000_0001, mask[0]);
            Assert.AreEqual (0b0000_0010, mask[1]);
        }

        [Test]
        public void SelectiveAckIgnoresNextExpectedAndOutOfRange ()
        {
            var received = new Dictionary<ushort, bool> { { 11, true }, { 10 + 2 + 256, true } };
            var mask = new byte[SelectiveAck.MaxLength];

            Assert.AreEqual (0, SelectiveAck.Build (10, received, mask));
        }

        [Test]
        public void WalksUnknownExtensions ()
        {
            var datagram = new List<byte> ();
            var header = new byte[UtpPacketCodec.HeaderLength];
            UtpPacketCodec.Write (header, new UtpHeader (UtpPacketType.Data, 5, 6, 7, 8, 9, 10), default, default);
            header[1] = 2;
            datagram.AddRange (header);
            datagram.AddRange (new byte[] { 1, 8, 0, 0, 0, 0, 0, 0, 0, 0 });
            datagram.AddRange (new byte[] { 9, 4, 0x05, 0, 0, 0 });
            datagram.AddRange (new byte[] { 0, 3, 0xAA, 0xBB, 0xCC });
            datagram.AddRange (Encoding.ASCII.GetBytes ("payload"));

            Assert.IsTrue (UtpPacketCodec.TryRead (datagram.ToArray (), out var decoded, out var sack, out var payload));
            Assert.AreEqual (UtpPacketType.Data, decoded.Type);
            Assert.AreEqual (4, sack.Length);
            Assert.IsTrue (SelectiveAck.IsSet (sack, 0));
            Assert.IsFalse (SelectiveAck.IsSet (sack, 1));
            Assert.IsTrue (SelectiveAck.IsSet (sack, 2));
            Assert.AreEqual ("payload", Encoding.ASCII.GetString (payload));
        }

        [Test]
        public void RejectsTruncatedExtension ()
        {
            var datagram = new byte[UtpPacketCodec.HeaderLength + 3];
            UtpPacketCodec.Write (datagram, new UtpHeader (UtpPacketType.State, 1, 0, 0, 0, 0, 0), default, default);
            datagram[1] = 1;
            datagram[20] = 0;
            datagram[21] = 4;

            Assert.IsFalse (UtpPacketCodec.TryRead (datagram, out _, out _, out _));
        }

        [Test]
        public void DistinguishesDhtFromUtp ()
        {
            var dht = Encoding.ASCII.GetBytes ("d1:ad2:id20:abcdefghij0123456789e1:q4:ping1:t2:aa1:y1:qe");
            var utp = new byte[UtpPacketCodec.HeaderLength];
            UtpPacketCodec.Write (utp, new UtpHeader (UtpPacketType.Syn, 1, 0, 0, 0, 0, 0), default, default);

            Assert.IsFalse (UtpSocketManager.IsUtpPacket (dht));
            Assert.IsTrue (UtpSocketManager.IsUtpPacket (utp));
            Assert.IsFalse (UtpSocketManager.IsUtpPacket (utp.AsSpan (0, 19)));
            utp[0] = 0x51;
            Assert.IsFalse (UtpSocketManager.IsUtpPacket (utp));
            utp[0] = 0x02;
            Assert.IsFalse (UtpSocketManager.IsUtpPacket (utp));
        }
    }
}
