using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MonoTorrent.Connections.Utp
{
    sealed class LossyUtpLink : IDisposable
    {
        public static readonly IPEndPoint EndPointA = new IPEndPoint (IPAddress.Parse ("10.0.0.1"), 6881);
        public static readonly IPEndPoint EndPointB = new IPEndPoint (IPAddress.Parse ("10.0.0.2"), 6882);

        readonly Channel<byte[]> toA = Channel.CreateUnbounded<byte[]> ();
        readonly Channel<byte[]> toB = Channel.CreateUnbounded<byte[]> ();
        readonly Random random;
        readonly Task pumpA;
        readonly Task pumpB;
        int finsFromA;
        int finsFromB;

        public LossyUtpLink (double drop, double duplicate, double reorder, int seed = 1234)
        {
            Drop = drop;
            Duplicate = duplicate;
            Reorder = reorder;
            random = new Random (seed);
            A = new UtpSocketManager ((data, remote) => Enqueue (toB, data, ref finsFromA));
            B = new UtpSocketManager ((data, remote) => Enqueue (toA, data, ref finsFromB));
            pumpA = Task.Run (() => Pump (toA.Reader, A, EndPointB));
            pumpB = Task.Run (() => Pump (toB.Reader, B, EndPointA));
        }

        public UtpSocketManager A { get; }

        public UtpSocketManager B { get; }

        public double Drop { get; set; }

        public double Duplicate { get; set; }

        public double Reorder { get; set; }

        public int DatagramsFromA { get; private set; }

        public int FinsFromA => Volatile.Read (ref finsFromA);

        public int FinsFromB => Volatile.Read (ref finsFromB);

        ValueTask Enqueue (Channel<byte[]> channel, ReadOnlyMemory<byte> data, ref int fins)
        {
            var copy = data.ToArray ();
            if (UtpPacketCodec.TryRead (copy, out var header, out _, out _) && header.Type == UtpPacketType.Fin)
                Interlocked.Increment (ref fins);

            int copies;
            lock (random) {
                if (channel == toB)
                    DatagramsFromA++;
                if (random.NextDouble () < Drop)
                    return default;
                copies = random.NextDouble () < Duplicate ? 2 : 1;
            }
            for (int i = 0; i < copies; i++)
                channel.Writer.TryWrite (copy);
            return default;
        }

        async Task Pump (ChannelReader<byte[]> reader, UtpSocketManager target, IPEndPoint from)
        {
            var held = new List<(byte[] Data, int Countdown)> ();
            while (await reader.WaitToReadAsync ().ConfigureAwait (false)) {
                while (reader.TryRead (out var data)) {
                    int holdFor = 0;
                    lock (random)
                        if (random.NextDouble () < Reorder)
                            holdFor = random.Next (1, 5);
                    if (holdFor > 0) {
                        held.Add ((data, holdFor));
                        continue;
                    }

                    target.HandlePacket (data, from);
                    for (int i = 0; i < held.Count; i++) {
                        var item = held[i];
                        if (--item.Countdown == 0) {
                            target.HandlePacket (item.Data, from);
                            held.RemoveAt (i--);
                        } else {
                            held[i] = item;
                        }
                    }
                }
                foreach (var item in held)
                    target.HandlePacket (item.Data, from);
                held.Clear ();
            }
        }

        public void Dispose ()
        {
            A.Dispose ();
            B.Dispose ();
            toA.Writer.TryComplete ();
            toB.Writer.TryComplete ();
            Task.WaitAll (new[] { pumpA, pumpB }, TimeSpan.FromSeconds (5));
        }
    }
}
