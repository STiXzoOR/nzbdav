namespace NzbWebDAV.Par2Recovery.Packets;

public class MainPacket : Par2Packet
{
    public const string PacketType = "PAR 2.0\0Main\0\0\0\0";

    public ulong SliceSize { get; private set; }
    public uint RecoverySetFileCount { get; private set; }
    public List<byte[]> RecoverySetFileIds { get; } = new();

    public MainPacket(Par2PacketHeader header) : base(header) { }

    protected override void ParseBody(byte[] body)
    {
        SliceSize = BitConverter.ToUInt64(body, 0);
        RecoverySetFileCount = BitConverter.ToUInt32(body, 8);
        var offset = 12;
        for (var i = 0; i < RecoverySetFileCount && offset + 16 <= body.Length; i++, offset += 16)
        {
            var id = new byte[16];
            Buffer.BlockCopy(body, offset, id, 0, 16);
            RecoverySetFileIds.Add(id);
        }
    }
}
