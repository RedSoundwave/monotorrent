using NUnit.Framework;

namespace MonoTorrent.Connections.Utp
{
    [TestFixture]
    public class UtpCongestionTests
    {
        const int Packet = 1420;

        [Test]
        public void RttEstimatorFollowsRfc6298 ()
        {
            var rtt = new RttEstimator ();
            Assert.AreEqual (RttEstimator.InitialRtoMicroseconds, rtt.RtoMicroseconds);

            rtt.AddSample (200_000);
            Assert.AreEqual (200_000, rtt.SmoothedRttMicroseconds);
            Assert.AreEqual (100_000, rtt.RttVarianceMicroseconds);
            Assert.AreEqual (600_000, rtt.RtoMicroseconds);

            rtt.AddSample (100_000);
            Assert.AreEqual (187_500, rtt.SmoothedRttMicroseconds);
            Assert.AreEqual (100_000, rtt.RttVarianceMicroseconds);
        }

        [Test]
        public void RttEstimatorClampsAndBacksOff ()
        {
            var rtt = new RttEstimator ();
            rtt.AddSample (1_000);
            Assert.AreEqual (RttEstimator.MinRtoMicroseconds, rtt.RtoMicroseconds);

            rtt.Backoff ();
            Assert.AreEqual (2 * RttEstimator.MinRtoMicroseconds, rtt.RtoMicroseconds);
            for (int i = 0; i < 20; i++)
                rtt.Backoff ();
            Assert.AreEqual (RttEstimator.MaxRtoMicroseconds, rtt.RtoMicroseconds);
        }

        [Test]
        public void DelayHistoryTracksMinimumAcrossWrap ()
        {
            var history = new DelayHistory ();
            history.Add (0xFFFF_FF00, 0);
            history.Add (0x0000_0100, 1_000);
            history.Add (0xFFFF_FE00, 2_000);

            Assert.AreEqual (0xFFFF_FE00, history.BaseDelay);
            Assert.AreEqual (0x300, history.GetQueuingDelay (0x0000_0100));
            Assert.AreEqual (0, history.GetQueuingDelay (0xFFFF_FD00));
        }

        [Test]
        public void DelayHistoryForgetsOldMinimum ()
        {
            var history = new DelayHistory ();
            history.Add (1_000, 0);
            history.Add (5_000, DelayHistory.BucketMicroseconds);
            Assert.AreEqual (1_000u, history.BaseDelay);

            history.Add (6_000, 2 * DelayHistory.BucketMicroseconds);
            Assert.AreEqual (5_000u, history.BaseDelay);
        }

        [Test]
        public void LedbatGrowsBelowTargetAndShrinksAboveTarget ()
        {
            var ledbat = new LedbatController (Packet, 1024 * 1024, 10 * Packet);
            ledbat.OnLoss ();
            int window = ledbat.Window;

            ledbat.OnAck (window, 0, true);
            Assert.AreEqual (window + LedbatController.MaxWindowIncreasePerRtt, ledbat.Window);

            window = ledbat.Window;
            ledbat.OnAck (window, 2 * LedbatController.TargetDelayMicroseconds, true);
            Assert.AreEqual (window - LedbatController.MaxWindowIncreasePerRtt, ledbat.Window);
        }

        [Test]
        public void LedbatDoesNotGrowWhenApplicationLimited ()
        {
            var ledbat = new LedbatController (Packet, 1024 * 1024, 4 * Packet);
            ledbat.OnAck (Packet, 0, false);
            Assert.AreEqual (4 * Packet, ledbat.Window);
        }

        [Test]
        public void LedbatSlowStartDoublesPerRtt ()
        {
            var ledbat = new LedbatController (Packet, 1024 * 1024, 4 * Packet);
            ledbat.OnAck (4 * Packet, 0, true);
            Assert.IsTrue (ledbat.InSlowStart);
            Assert.AreEqual (8 * Packet, ledbat.Window);
        }

        [Test]
        public void LedbatHalvesOnLossButNeverBelowOnePacket ()
        {
            var ledbat = new LedbatController (Packet, 1024 * 1024, 8 * Packet);
            ledbat.OnLoss ();
            Assert.AreEqual (4 * Packet, ledbat.Window);
            Assert.IsFalse (ledbat.InSlowStart);
            for (int i = 0; i < 10; i++)
                ledbat.OnTimeout ();
            Assert.AreEqual (Packet, ledbat.Window);
        }
    }
}
