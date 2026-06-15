namespace NzbWebDAV.Database.Models;

public class Par2RecoverySet
{
    public Guid Id { get; set; }
    public Guid DirectoryDavItemId { get; set; } // the release directory DavItem
    public byte[] RecoverySetId { get; set; } = [];
    public long SliceSize { get; set; }
    public int TotalRecoveryBlocks { get; set; }
}
