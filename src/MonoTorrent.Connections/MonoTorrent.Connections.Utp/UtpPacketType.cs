namespace MonoTorrent.Connections.Utp
{
    public enum UtpPacketType : byte
    {
        Data = 0,
        Fin = 1,
        State = 2,
        Reset = 3,
        Syn = 4,
    }
}
