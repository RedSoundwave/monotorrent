# MonoTorrent — NexusFlow Edition

[![NuGet](https://img.shields.io/badge/nuget-3.9.1--nexusflow-blue)](https://github.com/RedSoundwave/monotorrent/packages)
[![Build](https://github.com/RedSoundwave/monotorrent/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/RedSoundwave/monotorrent/actions/workflows/release.yml)

A maintained fork of [MonoTorrent](https://github.com/alanmcgovern/monotorrent) for use in the NexusFlow plugin ecosystem. Tracks upstream `master` and adds APIs needed by the downloader plugin.

## NexusFlow Additions

| Feature | API | BEP |
|---------|-----|-----|
| Sequential download | `TorrentSettingsBuilder.SequentialDownload = true` | — |
| Enumerate connected peers | `TorrentManager.Peers.ActiveConnections` | — |
| Disconnect a peer | `PeerId.Disconnect()` | — |
| Piece retraction (lt_donthave) | handled automatically | [BEP 54](https://www.bittorrent.org/beps/bep_0054.html) |

## NuGet

Package: `MonoTorrent` `[3.9.1-nexusflow]`

Feed: `https://nuget.pkg.github.com/RedSoundwave/index.json`

```xml
<PackageReference Include="MonoTorrent" Version="[3.9.1-nexusflow]" />
```

## Supported Specifications

Full list: [bittorrent.org/beps/bep_0000.html](http://www.bittorrent.org/beps/bep_0000.html)

### Final / Active
* BEP 3  — [The BitTorrent Protocol Specification](https://www.bittorrent.org/beps/bep_0003.html)
* BEP 20 — [Peer ID Conventions](http://www.bittorrent.org/beps/bep_0020.html)

### Accepted
* BEP 5  — [DHT Protocol](http://www.bittorrent.org/beps/bep_0005.html)
* BEP 6  — [Fast Extension](http://www.bittorrent.org/beps/bep_0006.html)
* BEP 7  — [IPv6 Tracker Extension](http://www.bittorrent.org/beps/bep_0007.html)
* BEP 9  — [Extension for Peers to Send Metadata Files](http://www.bittorrent.org/beps/bep_0009.html)
* BEP 10 — [Extension Protocol](http://www.bittorrent.org/beps/bep_0010.html)
* BEP 11 — [Peer Exchange (PEX)](http://www.bittorrent.org/beps/bep_0011.html)
* BEP 12 — [Multitracker Metadata Extension](http://www.bittorrent.org/beps/bep_0012.html)
* BEP 14 — [Local Service/Peer Discovery](http://www.bittorrent.org/beps/bep_0014.html)
* BEP 15 — [UDP Tracker Protocol](http://www.bittorrent.org/beps/bep_0015.html)
* BEP 19 — [HTTP/FTP/Web Seeding (GetRight-style)](http://www.bittorrent.org/beps/bep_0019.html)
* BEP 23 — [Tracker Returns Compact Peer Lists](http://www.bittorrent.org/beps/bep_0023.html)
* BEP 27 — [Private Torrents](http://www.bittorrent.org/beps/bep_0027.html)

### Draft
* BEP 16 — [Superseeding](http://www.bittorrent.org/beps/bep_0016.html)
* BEP 47 — [Padding files and extended file attributes](https://www.bittorrent.org/beps/bep_0047.html)
* BEP 48 — [Tracker Protocol Extension: Scrape](http://www.bittorrent.org/beps/bep_0048.html)
* BEP 52 — [The BitTorrent Protocol Specification v2](https://www.bittorrent.org/beps/bep_0052.html)
* BEP 54 — [The lt_donthave Extension](https://www.bittorrent.org/beps/bep_0054.html) *(NexusFlow addition)*

### Other
* [Message Stream Encryption (Vuze)](http://wiki.vuze.com/w/Message_Stream_Encryption)

## Client Features

* Prioritise specific files
* Selective file downloading (skip individual files)
* Sequential downloading for media / streaming
* Rarest-first piece picking with prioritisation
* End-game mode
* Per-torrent and global download/upload rate limiting
* In-memory cache to reduce disk reads
* Auto-throttle when download rate exceeds write rate
* IPv4 and IPv6 connections
* IP address ban lists
* Fast resume (no re-hash on restart)
* Incremental piece hashing
* Sparse files (NTFS)
* UPnP and NAT-PMP port forwarding
* Magnet URI support (including `so=` file pre-selection)
* Creating torrents from files or folders

## License

MIT — see [LICENSE](LICENSE). Original work by Alan McGovern and contributors.
