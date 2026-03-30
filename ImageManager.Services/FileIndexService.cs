using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class FileIndexService(ITrackingStore trackingStore) : IFileIndexService
{
    public async Task<int> ScanAndIndexAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await trackingStore.InitializeAsync(cancellationToken);
        var records = new List<FileRecord>(capacity: 4096);
        var count = 0;
        var roots = config.MainRoots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roots.Count == 0 && !string.IsNullOrWhiteSpace(config.MainRoot))
        {
            roots.Add(config.MainRoot);
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(path);
                if (!config.TrackExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new FileInfo(path);
                records.Add(new FileRecord
                {
                    SourceRoot = root,
                    RelativePath = Path.GetRelativePath(root, path),
                    Extension = ext,
                    SizeBytes = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    IsSelected = true,
                    VersionId = config.CurrentVersionId,
                    QualityTier = config.CurrentQualityTier
                });

                count++;
                if (records.Count >= 1000)
                {
                    await trackingStore.UpsertBatchAsync(records, cancellationToken);
                    progress?.Report($"Indexed {count} files...");
                    records.Clear();
                }
            }
        }

        if (records.Count > 0)
        {
            await trackingStore.UpsertBatchAsync(records, cancellationToken);
        }

        progress?.Report($"Index complete: {count} files.");
        return count;
    }
}
