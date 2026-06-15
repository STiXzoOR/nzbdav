using NzbWebDAV.Par2Recovery.Packets;

namespace NzbWebDAV.Par2Recovery;

public sealed class Par2RecoveryModel
{
    public required byte[] RecoverySetId { get; init; }
    public required long SliceSize { get; init; }
    public required List<Source> SourceFiles { get; init; }

    public sealed class Source
    {
        public required byte[] FileId { get; init; }
        public required string FileName { get; init; }
        public required ulong FileLength { get; init; }
        public long SliceSize { get; set; }
        public int SliceCount => SliceSize <= 0 ? 0 : (int)((FileLength + (ulong)SliceSize - 1) / (ulong)SliceSize);
    }

    public static async Task<Par2RecoveryModel?> ParseAsync(Stream stream, CancellationToken ct)
    {
        MainPacket? main = null;
        var fileDescs = new List<FileDesc>();
        byte[]? recoverySetId = null;
        await foreach (var p in Par2.ReadAllPackets(stream, ct).ConfigureAwait(false))
        {
            recoverySetId ??= p.Header.RecoverySetID;
            if (p is MainPacket mp) main = mp;
            else if (p is FileDesc fd) fileDescs.Add(fd);
        }
        if (main == null || recoverySetId == null) return null;

        var sliceSize = (long)main.SliceSize;
        var sources = fileDescs
            .GroupBy(f => Convert.ToHexString(f.FileID))
            .Select(g => g.First())
            .Select(f => new Source
            {
                FileId = f.FileID, FileName = f.FileName, FileLength = f.FileLength, SliceSize = sliceSize,
            })
            .ToList();
        return new Par2RecoveryModel { RecoverySetId = recoverySetId, SliceSize = sliceSize, SourceFiles = sources };
    }
}
