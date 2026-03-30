using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class ArchiveService(ITrackingStore trackingStore, IConflictPrompt? conflictPrompt = null) : IArchiveService
{
    public async Task<int> BackupToArchiveAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var records = await trackingStore.ReadSelectedAsync(cancellationToken);
        var count = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!config.ArchiveExtensions.Contains(record.Extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceRoot = string.IsNullOrWhiteSpace(record.SourceRoot) ? config.MainRoot : record.SourceRoot;
            var source = Path.Combine(sourceRoot, record.RelativePath);
            if (!File.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(config.ArchiveRoot, versionId, qualityTier, BuildTargetRelative(record, sourceRoot));
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, source, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(source, target, move: false);
            count++;
        }

        progress?.Report($"Archived {count} files.");
        return count;
    }

    public Task<int> CopyArchiveToStageAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.Combine(config.ArchiveRoot, versionId, qualityTier);
        return CopyFromRootAsync(sourceRoot, config.StageRoot, config, progress, cancellationToken);
    }

    public Task<int> CopyArchiveToMainAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.Combine(config.ArchiveRoot, versionId, qualityTier);
        return CopyFromRootAsync(sourceRoot, config.MainRoot, config, progress, cancellationToken);
    }

    public Task<int> CopyArchiveToMainFilteredAsync(LibraryConfig config, string versionId, string qualityTier, bool videosOnly, bool webpOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.Combine(config.ArchiveRoot, versionId, qualityTier);
        return CopyFromRootAsync(sourceRoot, config.MainRoot, config, progress, cancellationToken, videosOnly, webpOnly);
    }

    private Task<int> CopyFromRootAsync(
        string sourceRoot,
        string targetRoot,
        LibraryConfig config,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool videosOnly = false,
        bool webpOnly = false)
    {
        var count = 0;
        var videoExtensions = config.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sourceRoot))
        {
            return Task.FromResult(0);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (videosOnly && !videoExtensions.Contains(ext))
            {
                continue;
            }

            if (webpOnly && !ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = IsMainTarget(targetRoot, config)
                ? ResolveMainTargetPath(config, relative)
                : Path.Combine(targetRoot, relative);
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, file, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(file, target, move: false);
            count++;
        }

        progress?.Report($"Copied {count} archive files.");
        return Task.FromResult(count);
    }

    private static string BuildTargetRelative(FileRecord record, string sourceRoot)
    {
        var rootName = Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = "main";
        }

        return Path.Combine(rootName, record.RelativePath);
    }

    private static bool IsMainTarget(string targetRoot, LibraryConfig config) =>
        string.Equals(
            Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(config.MainRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveMainTargetPath(LibraryConfig config, string relativeFromStore)
    {
        var normalized = relativeFromStore.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var split = normalized.Split(Path.DirectorySeparatorChar, 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 2)
        {
            return Path.Combine(config.MainRoot, normalized);
        }

        var rootName = split[0];
        var rest = split[1];
        var root = config.MainRoots.FirstOrDefault(r =>
            string.Equals(Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), rootName, StringComparison.OrdinalIgnoreCase));
        root ??= config.MainRoot;
        return Path.Combine(root, rest);
    }
}
