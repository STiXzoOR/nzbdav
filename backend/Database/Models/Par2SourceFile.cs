namespace NzbWebDAV.Database.Models;

public class Par2SourceFile
{
    public Guid Id { get; set; }
    public Guid RecoverySetId { get; set; }   // FK -> Par2RecoverySet.Id
    public Guid DavItemId { get; set; }        // the posted source file's DavItem
    public long FileLength { get; set; }
    public int SliceCount { get; set; }
    public int FirstSliceIndex { get; set; }   // cumulative offset into global slice space
}
