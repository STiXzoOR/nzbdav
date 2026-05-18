using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;

namespace NzbWebDAV.Queue.PostProcessors;

public class EnsureFilesProducedValidator(DavDatabaseClient dbClient)
{
    public void ThrowIfValidationFails()
    {
        if (!HasAnyFile())
        {
            // When every file in the NZB is silently dropped — for example, when video files
            // with missing articles are caught by FileProcessor's `!IsVideoFile(filename)`
            // guard because their filename couldn't be resolved (empty/obfuscated) — the
            // mount folder ends up empty but no exception propagates. Without this check the
            // history row would be Completed, Sonarr/Radarr would try to import a directory
            // that contains nothing, and the cleanup `mode=history&name=delete` would later
            // tip over (cf. nzbdav-dev/nzbdav#364 — comment from emptyinthehead).
            throw new NoFilesProducedException(
                "No files were produced from the NZB. " +
                "Likely cause: all files had missing articles or were filtered out."
            );
        }
    }

    private bool HasAnyFile()
    {
        return dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Any(x => x.Type != DavItem.ItemType.Directory);
    }
}
