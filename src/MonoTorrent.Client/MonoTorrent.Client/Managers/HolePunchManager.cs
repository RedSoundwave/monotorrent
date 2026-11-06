using System;
using System.Collections.Generic;
using System.Net;

using MonoTorrent.Logging;
using MonoTorrent.Messages;

namespace MonoTorrent.Client
{
    class HolePunchManager
    {
        const int MaxTrackedPeers = 4096;

        static readonly Logger logger = Logger.Create (nameof (HolePunchManager));

        readonly TorrentManager manager;
        readonly Dictionary<Uri, PeerId> sources = new Dictionary<Uri, PeerId> ();
        readonly HashSet<Uri> requested = new HashSet<Uri> ();

        internal HolePunchManager (TorrentManager manager)
            => this.manager = manager;

        internal void RecordSource (IEnumerable<PeerInfo> peers, PeerId source)
        {
            if (!Supports (source))
                return;

            if (sources.Count >= MaxTrackedPeers)
                sources.Clear ();
            foreach (var peer in peers)
                sources[peer.ConnectionUri] = source;
        }

        internal bool TryRequestRendezvous (Peer peer)
        {
            var uri = peer.Info.ConnectionUri;
            if (!sources.TryGetValue (uri, out var relay) || relay.Disposed || !Supports (relay) || Matches (relay, ToEndPoint (uri)))
                return false;

            if (requested.Count >= MaxTrackedPeers)
                requested.Clear ();
            if (!requested.Add (uri))
                return false;

            logger.InfoFormatted (relay.Connection, "Hole punch: asking relay to introduce {0}", uri);
            Send (relay, Extended.HolePunchMessageType.Rendezvous, ToEndPoint (uri), Extended.HolePunchError.None);
            return true;
        }

        internal void Handle (PeerId sender, Extended.HolePunchMessage message)
        {
            if (!message.IsValid)
                return;

            var endPoint = message.EndPoint!;
            switch (message.MessageType) {
                case Extended.HolePunchMessageType.Rendezvous:
                    Relay (sender, endPoint);
                    break;
                case Extended.HolePunchMessageType.Connect:
                    manager.Engine?.ConnectionManager.ConnectForHolePunch (manager, endPoint);
                    break;
                case Extended.HolePunchMessageType.Error:
                    logger.InfoFormatted (sender.Connection, "Hole punch to {0} failed: {1}", endPoint, message.Error);
                    break;
            }
        }

        void Relay (PeerId initiator, IPEndPoint target)
        {
            var error = Extended.HolePunchError.None;
            PeerId? targetPeer = null;
            if (target.Port == 0 || target.Address.Equals (IPAddress.Any) || target.Address.Equals (IPAddress.IPv6Any))
                error = Extended.HolePunchError.NoSuchPeer;
            else if (Matches (initiator, target))
                error = Extended.HolePunchError.NoSelf;
            else if ((targetPeer = FindConnected (target)) is null)
                error = Extended.HolePunchError.NotConnected;
            else if (!Supports (targetPeer))
                error = Extended.HolePunchError.NoSupport;

            if (error != Extended.HolePunchError.None) {
                Send (initiator, Extended.HolePunchMessageType.Error, target, error);
                return;
            }

            logger.InfoFormatted (initiator.Connection, "Hole punch: relaying between this peer and {0}", target);
            Send (targetPeer!, Extended.HolePunchMessageType.Connect, ListenEndPoint (initiator), Extended.HolePunchError.None);
            Send (initiator, Extended.HolePunchMessageType.Connect, ListenEndPoint (targetPeer!), Extended.HolePunchError.None);
        }

        PeerId? FindConnected (IPEndPoint endPoint)
        {
            foreach (var peer in manager.Peers.ConnectedPeers)
                if (!peer.Disposed && Matches (peer, endPoint))
                    return peer;
            return null;
        }

        static bool Supports (PeerId peer)
            => peer.SupportsLTMessages && peer.ExtensionSupports.Supports (MessageEncoder.Extended.HolePunchSupport.Name);

        static bool Matches (PeerId peer, IPEndPoint endPoint)
        {
            var uri = peer.Peer.Info.ConnectionUri;
            return IPAddress.Parse (uri.Host).Equals (endPoint.Address)
                && (uri.Port == endPoint.Port || peer.Peer.LocalPort == endPoint.Port);
        }

        static IPEndPoint ListenEndPoint (PeerId peer)
        {
            var uri = peer.Peer.Info.ConnectionUri;
            var port = peer.Peer.LocalPort > 0 ? peer.Peer.LocalPort : uri.Port;
            return new IPEndPoint (IPAddress.Parse (uri.Host), port);
        }

        static IPEndPoint ToEndPoint (Uri uri)
            => new IPEndPoint (IPAddress.Parse (uri.Host), uri.Port);

        static void Send (PeerId peer, Extended.HolePunchMessageType type, IPEndPoint endPoint, Extended.HolePunchError error)
            => peer.MessageQueue.Enqueue (MessageEncoder.Extended.WriteHolePunch (peer.ExtensionSupports, type, endPoint, error));
    }
}
