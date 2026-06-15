using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue.PostProcessors;
using NzbWebDAV.Services.Repair;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// This service monitors for health checks
/// </summary>
public class HealthCheckService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly INntpClient _usenetClient;
    private readonly WebsocketManager _websocketManager;

    private static readonly Dictionary<string, DateTimeOffset> _missingSegmentIds = new();
    private static readonly TimeSpan _missingCacheTtl = TimeSpan.FromHours(6);

    public HealthCheckService
    (
        ConfigManager configManager,
        UsenetStreamingClient usenetClient,
        WebsocketManager websocketManager
    )
    {
        _configManager = configManager;
        _usenetClient = usenetClient;
        _websocketManager = websocketManager;

        _configManager.OnConfigChanged += (_, configEventArgs) =>
        {
            // when usenet host changes, clear the missing segments cache
            if (!configEventArgs.ChangedConfig.ContainsKey("usenet.host")) return;
            lock (_missingSegmentIds) _missingSegmentIds.Clear();
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // if the repair-job is disabled, then don't do anything
                if (!_configManager.IsRepairJobEnabled())
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // get concurrency
                var concurrency = _configManager.GetUsenetProviderConfig().TotalPooledConnections;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                // get the davItem to health-check
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var currentDateTime = DateTimeOffset.UtcNow;
                var davItem = await GetHealthCheckQueueItems(dbClient)
                    .Where(x => x.NextHealthCheck == null || x.NextHealthCheck < currentDateTime)
                    .FirstOrDefaultAsync(cts.Token).ConfigureAwait(false);

                // if there is no item to health-check, don't do anything
                if (davItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(false);
                    continue;
                }

                // perform the health check
                await PerformHealthCheck(davItem, dbClient, concurrency, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                // OperationCanceledException is expected on sigterm
                return;
            }
            catch (Exception e)
            {
                Log.Error(e, $"Unexpected error performing background health checks: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public static IOrderedQueryable<DavItem> GetHealthCheckQueueItems(DavDatabaseClient dbClient)
    {
        return GetHealthCheckQueueItemsQuery(dbClient)
            .OrderBy(x => x.NextHealthCheck)
            .ThenByDescending(x => x.ReleaseDate)
            .ThenBy(x => x.Id);
    }

    public static IQueryable<DavItem> GetHealthCheckQueueItemsQuery(DavDatabaseClient dbClient)
    {
        return dbClient.Ctx.Items
            .Where(x => x.Type == DavItem.ItemType.UsenetFile)
            .Where(x => x.HistoryItemId == null);
    }

    private async Task PerformHealthCheck
    (
        DavItem davItem,
        DavDatabaseClient dbClient,
        int concurrency,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;

        // (1) minimum-age guard: don't health-check (and possibly delete) very fresh
        // releases — articles may not have fully propagated across providers yet.
        if (davItem.ReleaseDate is { } rd && now - rd < _configManager.GetRepairMinimumAge())
        {
            davItem.LastHealthCheck = now;
            davItem.NextHealthCheck = now + _configManager.GetRecheckBackoff();
            await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        // (2) classify every segment across all providers
        var segments = await GetAllSegments(davItem, dbClient, ct).ConfigureAwait(false);
        if (davItem.ReleaseDate == null) await UpdateReleaseDate(davItem, segments, ct).ConfigureAwait(false);

        // setup progress tracking (preserve original debounced websocket progress behavior)
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));
        void ReportProgress(int percent)
        {
            var message = $"{davItem.Id}|{percent}";
            debounce(() => _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, message));
        }

        // STAT every input segment across all providers, bounded by the provider pool size so a
        // single hung STAT can't stall the whole loop (a per-STAT timeout turns a hung read into a
        // TransientError -> Inconclusive). WithConcurrencyAsync yields results OUT OF ORDER, so each
        // result carries its original index and is placed back at statuses[i] to preserve the
        // segments[i] <-> statuses[i] alignment that BuildMissingRanges' par2 byte-mapping depends on.
        var statTimeout = _configManager.GetStatTimeout();
        var statuses = new SegmentStatus[segments.Count];
        // Fail safe: default any (never-expected) unwritten slot to Inconclusive so it can
        // never read as Present/Missing and influence a deletion. Every index is written below.
        Array.Fill(statuses, SegmentStatus.Inconclusive);
        var processed = 0;
        var indexed = segments.Select(async (segmentId, i) =>
        {
            var outcomes = await _usenetClient.StatAllProvidersAsync(segmentId, statTimeout, ct).ConfigureAwait(false);
            return (Index: i, Status: SegmentClassifier.Classify(outcomes));
        });
        await foreach (var r in indexed.WithConcurrencyAsync(Math.Max(1, concurrency)).ConfigureAwait(false))
        {
            statuses[r.Index] = r.Status;
            processed++;
            if (segments.Count > 0) ReportProgress(processed * 100 / segments.Count);
        }
        _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
        _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|done");

        // (3) verdict — par2-aware recoverability is the PRIMARY path; segment-ratio is the fallback.
        //
        // par2 mapping note (per-subtype byte-range availability):
        //   - DavNzbFile    : stores only SegmentIds[] with NO per-segment byte sizes.
        //   - DavRarFile    : RarParts[] carry per-PART byte ranges (Offset/ByteCount) but
        //                     NOT per-segment ranges (a part flattens to many segments).
        //   - DavMultipartFile : FileParts[] carry per-PART SegmentIdByteRange/FilePartByteRange
        //                     but, again, not per-individual-segment ranges.
        // GetAllSegments flattens every subtype into one positional segment list aligned 1:1 with
        // `statuses`, discarding the part structure. Since no subtype exposes a clean per-SEGMENT
        // byte offset (only per-part), we use the equal-size approximation FileLength/segmentCount
        // uniformly for all subtypes. This is acceptable and bounded: a missing segment damages at
        // most ceil(perSegment/sliceSize)+1 slices, and the verdict is further gated by the
        // all-provider STAT, the strike machine, and the minimum-age guard before any deletion.
        var par2Set = _configManager.IsPar2RecoveryEnabled()
            ? await dbClient.Ctx.Par2RecoverySets
                .FirstOrDefaultAsync(s => s.DirectoryDavItemId == davItem.ParentId, ct).ConfigureAwait(false)
            : null;

        Par2SourceFile? sourceRow = null;
        if (par2Set != null && statuses.All(s => s != SegmentStatus.Inconclusive))
        {
            sourceRow = await dbClient.Ctx.Par2SourceFiles
                .FirstOrDefaultAsync(f => f.RecoverySetId == par2Set.Id && f.DavItemId == davItem.Id, ct)
                .ConfigureAwait(false);
        }

        // STAT the recovery volumes (all-provider) only when we actually have a tracked source file
        // to recover. Inconclusive on any volume segment must win (never delete on unconfirmed data).
        var availableRecoveryBlocks = 0;
        var anyVolumeInconclusive = false;
        if (par2Set != null && sourceRow != null)
        {
            var volumes = await dbClient.Ctx.Par2RecoveryVolumes
                .Where(v => v.RecoverySetId == par2Set.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            var volStatuses = new List<(int BlockCount, bool AllArticlesPresent)>(volumes.Count);
            foreach (var vol in volumes)
            {
                var allPresent = true;
                foreach (var seg in vol.SegmentIds)
                {
                    ct.ThrowIfCancellationRequested();
                    var st = SegmentClassifier.Classify(
                        await _usenetClient.StatAllProvidersAsync(seg, statTimeout, ct).ConfigureAwait(false));
                    if (st == SegmentStatus.Inconclusive) { anyVolumeInconclusive = true; allPresent = false; break; }
                    if (st != SegmentStatus.Present) allPresent = false;
                }
                if (anyVolumeInconclusive) break;
                volStatuses.Add((vol.BlockCount, allPresent));
            }
            if (!anyVolumeInconclusive)
                availableRecoveryBlocks = Par2RecoveryCalculator.AvailableRecoveryBlocks(volStatuses);
        }

        var verdict = DecideVerdict(
            statuses, segments, par2Set, sourceRow,
            availableRecoveryBlocks, anyVolumeInconclusive,
            _configManager.GetMaxMissingSegmentRatio());

        // (4) strike machine
        var strike = StrikeMachine.Next(verdict, davItem.HealthCheckFailureCount, davItem.FirstFailedHealthCheck,
            now, _configManager.GetMinimumFailureWindow(), _configManager.GetRecheckBackoff(),
            _configManager.GetRequiredConsecutiveFailures());

        // (5) persist strike state + schedule
        davItem.HealthCheckFailureCount = strike.NewCount;
        davItem.FirstFailedHealthCheck = strike.NewFirstFailed;
        davItem.LastHealthCheck = now;
        davItem.NextHealthCheck = strike.NextCheck;

        // (6) record result row
        var (res, repairStatus, msg) = verdict switch
        {
            FileHealthVerdict.Healthy =>
                (HealthCheckResult.HealthResult.Healthy, HealthCheckResult.RepairAction.None, "File is healthy."),
            FileHealthVerdict.Inconclusive =>
                (HealthCheckResult.HealthResult.Unhealthy, HealthCheckResult.RepairAction.ActionNeeded,
                 "Inconclusive (provider error/timeout); not actioned, rescheduled."),
            _ => (HealthCheckResult.HealthResult.Unhealthy, HealthCheckResult.RepairAction.ActionNeeded,
                 $"Missing articles confirmed (strike {strike.NewCount}/{_configManager.GetRequiredConsecutiveFailures()})."),
        };
        dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
        {
            Id = Guid.NewGuid(),
            DavItemId = davItem.Id,
            Path = davItem.Path,
            CreatedAt = now,
            Result = res,
            RepairStatus = repairStatus,
            Message = msg,
        }));
        await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // (7) repair only when confirmed dead across the window
        if (strike.ShouldRepair)
            await Repair(davItem, dbClient, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure verdict decision (no I/O). The async recovery-volume STAT is performed by the caller
    /// and threaded in as <paramref name="availableRecoveryBlocks"/>/<paramref name="anyVolumeInconclusive"/>.
    ///
    /// Precedence:
    ///   1. par2 disabled or no recovery set for this directory  → segment-ratio fallback.
    ///   2. any INPUT segment Inconclusive                       → Inconclusive (dominates; never deletes).
    ///   3. no par2 source-file row for this dav item            → segment-ratio fallback.
    ///   4. any RECOVERY-VOLUME segment Inconclusive             → Inconclusive (dominates).
    ///   5. otherwise par2 recoverability: recoverable→Healthy, else DefinitivelyMissing.
    /// </summary>
    internal static FileHealthVerdict DecideVerdict(
        IReadOnlyList<SegmentStatus> statuses,
        IReadOnlyList<string> segments,
        Par2RecoverySet? par2Set,
        Par2SourceFile? sourceRow,
        int availableRecoveryBlocks,
        bool anyVolumeInconclusive,
        double maxMissingRatio)
    {
        if (par2Set == null)
            return SegmentRatioEvaluator.Evaluate(statuses, maxMissingRatio);

        if (statuses.Any(s => s == SegmentStatus.Inconclusive))
            return FileHealthVerdict.Inconclusive;

        // This dav item isn't a tracked par2 source file → fall back to the ratio path.
        if (sourceRow == null)
            return SegmentRatioEvaluator.Evaluate(statuses, maxMissingRatio);

        if (anyVolumeInconclusive)
            return FileHealthVerdict.Inconclusive;

        var ranges = BuildMissingRanges(segments, statuses, sourceRow, par2Set.SliceSize);
        var damaged = Par2RecoveryCalculator.CountDamagedSlices(ranges);
        return Par2RecoveryCalculator.IsRecoverable(damaged, availableRecoveryBlocks)
            ? FileHealthVerdict.Healthy
            : FileHealthVerdict.DefinitivelyMissing;
    }

    /// <summary>
    /// Map this file's definitively-missing segments to par2 slice ranges, relative to the file's
    /// <see cref="Par2SourceFile.FirstSliceIndex"/>. No subtype persists per-SEGMENT byte offsets
    /// (only per-part ranges on Rar/Multipart, which GetAllSegments flattens away), so we
    /// approximate every segment's size as FileLength/segmentCount uniformly. CountDamagedSlices
    /// only needs the COUNT of distinct damaged slices, so absolute slice numbering is irrelevant.
    /// </summary>
    internal static IReadOnlyCollection<Par2RecoveryCalculator.MissingRange> BuildMissingRanges(
        IReadOnlyList<string> segments, IReadOnlyList<SegmentStatus> statuses, Par2SourceFile src, long sliceSize)
    {
        var ranges = new List<Par2RecoveryCalculator.MissingRange>();
        var perSegment = segments.Count > 0 ? src.FileLength / segments.Count : 0;
        if (perSegment <= 0) return ranges;
        for (var i = 0; i < segments.Count; i++)
        {
            if (i >= statuses.Count || statuses[i] != SegmentStatus.DefinitivelyMissing) continue;
            var byteStart = perSegment * i;
            ranges.Add(new Par2RecoveryCalculator.MissingRange(src.FirstSliceIndex, sliceSize, byteStart, perSegment));
        }
        return ranges;
    }

    private async Task UpdateReleaseDate(DavItem davItem, List<string> segments, CancellationToken ct)
    {
        var firstSegmentId = StringUtil.EmptyToNull(segments.FirstOrDefault());
        if (firstSegmentId == null) return;
        var articleHeadersResponse = await _usenetClient.HeadAsync(firstSegmentId, ct).ConfigureAwait(false);
        var articleHeaders = articleHeadersResponse.ArticleHeaders!;
        davItem.ReleaseDate = articleHeaders.Date;
    }

    private async Task<List<string>> GetAllSegments(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        if (davItem.SubType == DavItem.ItemSubType.NzbFile)
        {
            var nzbFile = await dbClient.GetDavNzbFileAsync(davItem, ct).ConfigureAwait(false);
            return nzbFile?.SegmentIds?.ToList() ?? [];
        }

        if (davItem.SubType == DavItem.ItemSubType.RarFile)
        {
            var rarFile = await dbClient.GetDavRarFileAsync(davItem, ct).ConfigureAwait(false);
            return rarFile?.RarParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        if (davItem.SubType == DavItem.ItemSubType.MultipartFile)
        {
            var multipartFile = await dbClient.GetDavMultipartFileAsync(davItem, ct).ConfigureAwait(false);
            return multipartFile?.Metadata?.FileParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        return [];
    }

    private async Task Repair(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        try
        {
            // this method is only reached for confirmed-dead files, so any segment
            // id of this item is now definitively missing. Cache them (with a TTL)
            // so streaming read-paths fail fast instead of re-hitting dead articles.
            var deadSegments = await GetAllSegments(davItem, dbClient, ct).ConfigureAwait(false);

            // if the file pattern has been marked as ignored,
            // then don't bother trying to repair it. We can simply delete it.
            var blocklistedFiles = _configManager.GetBlocklistedFiles();
            if (BlocklistedFilePostProcessor.MatchesAnyPattern(davItem.Name, blocklistedFiles))
            {
                dbClient.Ctx.Items.Remove(davItem);
                CacheMissingSegments(deadSegments);
                dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
                {
                    Id = Guid.NewGuid(),
                    DavItemId = davItem.Id,
                    Path = davItem.Path,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Result = HealthCheckResult.HealthResult.Unhealthy,
                    RepairStatus = HealthCheckResult.RepairAction.Deleted,
                    Message = string.Join(" ", [
                        "File had missing articles.",
                        "Filename pattern is marked in settings as an ignored (unwanted) file.",
                        "Deleted file."
                    ])
                }));
                await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }

            // if the unhealthy item is unlinked/orphaned,
            // then we can simply delete it.
            var symlinkOrStrmPath = OrganizedLinksUtil.GetLink(davItem, _configManager);
            if (symlinkOrStrmPath == null)
            {
                dbClient.Ctx.Items.Remove(davItem);
                CacheMissingSegments(deadSegments);
                dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
                {
                    Id = Guid.NewGuid(),
                    DavItemId = davItem.Id,
                    Path = davItem.Path,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Result = HealthCheckResult.HealthResult.Unhealthy,
                    RepairStatus = HealthCheckResult.RepairAction.Deleted,
                    Message = string.Join(" ", [
                        "File had missing articles.",
                        "Could not find corresponding symlink or strm-file within Library Dir.",
                        "Deleted file."
                    ])
                }));
                await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }

            // if the unhealthy item is linked within the organized media-library
            // then we must find the corresponding arr instance and trigger a new search.
            var linkType = symlinkOrStrmPath.ToLower().EndsWith("strm") ? "strm-file" : "symlink";
            foreach (var arrClient in _configManager.GetArrConfig().GetArrClients())
            {
                var rootFolders = await arrClient.GetRootFolders().ConfigureAwait(false);
                if (!rootFolders.Any(x => symlinkOrStrmPath.StartsWith(x.Path!))) continue;

                // if we found a corresponding arr instance,
                // then remove and search.
                if (await arrClient.RemoveAndSearch(symlinkOrStrmPath).ConfigureAwait(false))
                {
                    dbClient.Ctx.Items.Remove(davItem);
                    CacheMissingSegments(deadSegments);
                    dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
                    {
                        Id = Guid.NewGuid(),
                        DavItemId = davItem.Id,
                        Path = davItem.Path,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Result = HealthCheckResult.HealthResult.Unhealthy,
                        RepairStatus = HealthCheckResult.RepairAction.Repaired,
                        Message = string.Join(" ", [
                            "File had missing articles.",
                            $"Corresponding {linkType} found within Library Dir.",
                            "Triggered new Arr search."
                        ])
                    }));
                    await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                    return;
                }

                // if we could not find corresponding media-item to remove-and-search
                // within the found arr instance, then break out of this loop so that
                // we can fall back to the behavior below of deleting both the link-file
                // and the dav-item.
                break;
            }

            // if we could not find a corresponding arr instance
            // then we can delete both the item and the link-file.
            await Task.Run(() => File.Delete(symlinkOrStrmPath)).ConfigureAwait(false);
            dbClient.Ctx.Items.Remove(davItem);
            CacheMissingSegments(deadSegments);
            dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                Result = HealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = HealthCheckResult.RepairAction.Deleted,
                Message = string.Join(" ", [
                    "File had missing articles.",
                    $"Corresponding {linkType} found within Library Dir.",
                    "Could not find corresponding Radarr/Sonarr media-item to trigger a new search.",
                    $"Deleted the webdav-file and {linkType}."
                ])
            }));
            await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // if an error is encountered during repairs,
            // then mark the item as unhealthy, and check again in a day.
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = utcNow + TimeSpan.FromDays(1);
            dbClient.Ctx.HealthCheckResults.Add(SendStatus(new HealthCheckResult()
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = utcNow,
                Result = HealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = HealthCheckResult.RepairAction.ActionNeeded,
                Message = $"Error performing file repair: {e.Message}"
            }));
            await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private HealthCheckResult SendStatus(HealthCheckResult result)
    {
        _ = _websocketManager.SendMessage
        (
            WebsocketTopic.HealthItemStatus,
            $"{result.DavItemId}|{(int)result.Result}|{(int)result.RepairStatus}"
        );
        return result;
    }

    public static void CheckCachedMissingSegmentIds(IEnumerable<string> segmentIds)
    {
        lock (_missingSegmentIds)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var segmentId in segmentIds)
                if (_missingSegmentIds.TryGetValue(segmentId, out var ts))
                {
                    if (now - ts < _missingCacheTtl) throw new UsenetArticleNotFoundException(segmentId);
                    _missingSegmentIds.Remove(segmentId); // expired → re-verify on next read
                }
        }
    }

    private static void CacheMissingSegments(IEnumerable<string> segmentIds)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_missingSegmentIds)
            foreach (var id in segmentIds)
                _missingSegmentIds[id] = now;
    }
}