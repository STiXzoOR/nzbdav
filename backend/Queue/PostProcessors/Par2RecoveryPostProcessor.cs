using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Par2Recovery;
using Serilog;

namespace NzbWebDAV.Queue.PostProcessors;

/// <summary>
/// Builds and persists a par2 recovery model BEFORE the blocklist post-processor
/// deletes the *.par2 DavItems (and their DavNzbFile.SegmentIds) from the change tracker.
///
/// Must run while the par2 DavItems + their DavNzbFiles are still EntityState.Added and
/// before the single final SaveChanges (it adds Par2* rows to the same change set).
///
/// directoryDavItemId is the release/mount-folder DavItem.Id — i.e. the directory whose
/// children are the posted source files. Source files' ParentId == directoryDavItemId, which
/// is exactly how the health-check task later looks up the recovery set
/// (Par2RecoverySets where DirectoryDavItemId == davItem.ParentId).
/// </summary>
public class Par2RecoveryPostProcessor(DavDatabaseClient dbClient, INntpClient usenetClient)
{
    public async Task PersistRecoveryModel(Guid directoryDavItemId, CancellationToken ct)
    {
        try
        {
            var added = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
                .Where(x => x.State == EntityState.Added && x.Entity.Type != DavItem.ItemType.Directory)
                .Select(x => x.Entity)
                .ToList();

            var par2Items = added.Where(x => x.Name.ToLower().EndsWith(".par2")).ToList();
            if (par2Items.Count == 0) return;

            // The index par2 is the one that is NOT a recovery volume (no `.volX+Y.par2`).
            // par2cmdline always emits a plain `name.par2` index file alongside volumes.
            var indexItem = par2Items.FirstOrDefault(x => !Par2.ParVolume.IsMatch(x.Name)) ?? par2Items.First();

            var addedNzbFiles = dbClient.Ctx.ChangeTracker.Entries<DavNzbFile>()
                .Where(x => x.State == EntityState.Added)
                .Select(x => x.Entity)
                .ToDictionary(x => x.Id, x => x);

            if (!addedNzbFiles.TryGetValue(indexItem.Id, out var indexNzb)) return;

            // FileProcessor/FileAggregator always populate DavItem.FileSize for par2 files
            // (it falls back to INntpClient.GetFileSizeAsync(NzbFile) at processing time).
            // We can't recompute it here cheaply (no NzbFile in the change tracker), so if it's
            // somehow absent we bail to the ratio-based fallback rather than guess.
            var size = indexItem.FileSize;
            if (size is null or <= 0) return;

            await using var stream = usenetClient.GetFileStream(indexNzb.SegmentIds, size.Value, articleBufferSize: 0);
            var model = await Par2RecoveryModel.ParseAsync(stream, ct).ConfigureAwait(false);
            if (model == null) return;

            // Map posted source files by name so we can attach DavItem ids to the par2 source records.
            var byName = added
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First());

            var recoverySetId = Guid.NewGuid();
            var sourceRows = new List<Par2SourceFile>();
            var cumulative = 0;
            foreach (var src in model.SourceFiles)
            {
                if (!byName.TryGetValue(src.FileName, out var davItem)) continue;
                sourceRows.Add(new Par2SourceFile
                {
                    Id = Guid.NewGuid(),
                    RecoverySetId = recoverySetId,
                    DavItemId = davItem.Id,
                    FileLength = (long)src.FileLength,
                    SliceCount = src.SliceCount,
                    FirstSliceIndex = cumulative,
                });
                cumulative += src.SliceCount;
            }

            if (sourceRows.Count == 0) return;

            var volumeRows = new List<Par2RecoveryVolume>();
            var totalBlocks = 0;
            foreach (var vol in par2Items)
            {
                if (!Par2.ParVolume.IsMatch(vol.Name)) continue;

                // par2cmdline names volumes `name.volX+Y.par2` (minimal width); the `+Y`
                // block count lives between the last `+` and the trailing `.par2`.
                var plus = vol.Name.LastIndexOf('+');
                var dot = vol.Name.ToLower().LastIndexOf(".par2", StringComparison.Ordinal);
                if (plus < 0 || dot < 0 || dot <= plus) continue;
                if (!int.TryParse(vol.Name.AsSpan(plus + 1, dot - plus - 1), out var blocks)) continue;

                if (!addedNzbFiles.TryGetValue(vol.Id, out var volNzb)) continue;

                volumeRows.Add(new Par2RecoveryVolume
                {
                    Id = Guid.NewGuid(),
                    RecoverySetId = recoverySetId,
                    BlockCount = blocks,
                    SegmentIds = volNzb.SegmentIds,
                });
                totalBlocks += blocks;
            }

            dbClient.Ctx.Par2RecoverySets.Add(new Par2RecoverySet
            {
                Id = recoverySetId,
                DirectoryDavItemId = directoryDavItemId,
                RecoverySetId = model.RecoverySetId,
                SliceSize = model.SliceSize,
                TotalRecoveryBlocks = totalBlocks,
            });
            dbClient.Ctx.Par2SourceFiles.AddRange(sourceRows);
            dbClient.Ctx.Par2RecoveryVolumes.AddRange(volumeRows);

            // Do NOT SaveChanges here — the import flow saves once; we add to the same change set.
        }
        catch (Exception e)
        {
            Log.Warning($"Par2 recovery modeling failed (falling back to ratio): {e.Message}");
            // swallow — never block import on par2 modeling
        }
    }
}
