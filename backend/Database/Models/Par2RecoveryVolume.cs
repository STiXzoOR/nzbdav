namespace NzbWebDAV.Database.Models;

public class Par2RecoveryVolume
{
    public Guid Id { get; set; }
    public Guid RecoverySetId { get; set; }    // FK -> Par2RecoverySet.Id
    public int BlockCount { get; set; }
    public string[] SegmentIds { get; set; } = [];
}
