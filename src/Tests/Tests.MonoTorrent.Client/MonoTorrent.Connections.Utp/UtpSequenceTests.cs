using NUnit.Framework;

namespace MonoTorrent.Connections.Utp
{
    [TestFixture]
    public class UtpSequenceTests
    {
        [Test]
        public void ComparesAcrossWrap ()
        {
            Assert.IsTrue (UtpSequence.IsLess (0xFFFF, 0));
            Assert.IsTrue (UtpSequence.IsLess (0xFFF0, 0x0010));
            Assert.IsFalse (UtpSequence.IsLess (0, 0xFFFF));
            Assert.IsFalse (UtpSequence.IsLess (5, 5));
            Assert.IsTrue (UtpSequence.IsLessOrEqual (5, 5));
            Assert.IsTrue (UtpSequence.IsLessOrEqual (0xFFFF, 0));
            Assert.IsFalse (UtpSequence.IsLessOrEqual (1, 0xFFFF));
        }

        [Test]
        public void ComparesWithoutWrap ()
        {
            Assert.IsTrue (UtpSequence.IsLess (1, 2));
            Assert.IsFalse (UtpSequence.IsLess (2, 1));
            Assert.IsTrue (UtpSequence.IsLess (0, 0x7FFF));
            Assert.IsFalse (UtpSequence.IsLess (0, 0x8000));
        }

        [Test]
        public void DistanceAcrossWrap ()
        {
            Assert.AreEqual (1, UtpSequence.Distance (0xFFFF, 0));
            Assert.AreEqual (0x20, UtpSequence.Distance (0xFFF0, 0x10));
            Assert.AreEqual (0xFFFF, UtpSequence.Distance (1, 0));
            Assert.AreEqual (0, UtpSequence.Distance (7, 7));
        }
    }
}
