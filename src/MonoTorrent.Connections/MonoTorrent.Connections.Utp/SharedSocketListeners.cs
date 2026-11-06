using System;
using System.Net;

using MonoTorrent.Connections.Dht;
using MonoTorrent.Connections.Peer;

using ReusableTasks;

namespace MonoTorrent.Connections.Utp
{
    abstract class SharedSocketView : IListener
    {
        protected SharedSocketView (SharedUdpSocket owner)
            => Owner = owner;

        public event EventHandler<EventArgs>? StatusChanged;

        public IPEndPoint? LocalEndPoint => Status == ListenerStatus.Listening ? Owner.LocalEndPoint : null;

        protected SharedUdpSocket Owner { get; }

        public IPEndPoint PreferredLocalEndPoint => Owner.PreferredLocalEndPoint;

        public ListenerStatus Status { get; private set; } = ListenerStatus.NotListening;

        public void Start ()
        {
            if (Status == ListenerStatus.Listening)
                return;
            SetStatus (Owner.Acquire ());
        }

        public void Stop ()
        {
            if (Status != ListenerStatus.Listening)
                return;
            Owner.Release ();
            SetStatus (ListenerStatus.NotListening);
        }

        protected virtual void OnStatusChanged ()
        {
        }

        void SetStatus (ListenerStatus status)
        {
            Status = status;
            OnStatusChanged ();
            StatusChanged?.Invoke (this, EventArgs.Empty);
        }
    }

    sealed class SharedDhtListener : SharedSocketView, IDhtListener
    {
        public SharedDhtListener (SharedUdpSocket owner)
            : base (owner)
        {
        }

        public event Action<ReadOnlyMemory<byte>, CompactEndPoint>? MessageReceived;

        public async ReusableTask SendAsync (ReadOnlyMemory<byte> buffer, CompactEndPoint endpoint)
        {
            if (Status == ListenerStatus.PortNotFree)
                throw new InvalidOperationException ($"The listener could not bind to {PreferredLocalEndPoint}. Choose a new listening endpoint.");
            if (Status == ListenerStatus.NotListening)
                throw new InvalidOperationException ("You must invoke StartAsync before sending or receiving a message with this listener.");

            await Owner.SendAsync (buffer, new IPEndPoint (new IPAddress (endpoint.Address), endpoint.Port)).ConfigureAwait (false);
        }

        internal void Receive (ReadOnlyMemory<byte> message, CompactEndPoint from)
        {
            if (Status == ListenerStatus.Listening)
                MessageReceived?.Invoke (message, from);
        }
    }

    sealed class SharedUtpPeerListener : SharedSocketView, IPeerConnectionListener
    {
        public SharedUtpPeerListener (SharedUdpSocket owner)
            : base (owner)
        {
        }

        public event EventHandler<PeerConnectionEventArgs>? ConnectionReceived;

        internal void Accept (UtpConnection connection)
        {
            if (Status != ListenerStatus.Listening) {
                connection.Dispose ();
                return;
            }
            ConnectionReceived?.Invoke (this, new PeerConnectionEventArgs (new UtpPeerConnection (connection), null));
        }

        protected override void OnStatusChanged ()
            => Owner.SetAcceptIncoming (Status == ListenerStatus.Listening);
    }
}
