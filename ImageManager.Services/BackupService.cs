using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class BackupService(ITrackingStore trackingStore, IConflictPrompt? conflictPrompt = null) : IBackupService
{
    public async Task<int> RunBackupAsync(LibraryConfig config, bool currentVersionOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var records = await trackingStore.ReadSelectedAsync(cancellationToken);
        var count = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentVersionOnly && !string.Equals(record.VersionId, config.CurrentVersionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceRoot = string.IsNullOrWhiteSpace(record.SourceRoot) ? config.MainRoot : record.SourceRoot;
            var source = Path.Combine(sourceRoot, record.RelativePath);
            var relativeTarget = BuildTargetRelative(record, sourceRoot);
            var backupTarget = Path.Combine(config.BackupRoot, relativeTarget);
            var fullTarget = Path.Combine(config.FullBackupRoot, relativeTarget);
            if (!File.Exists(source))
            {
                continue;
            }

            if (FileTransferService.ShouldProcess(config.ConflictPolicy, source, backupTarget, conflictPrompt))
            {
                FileTransferService.CopyOrMove(source, backupTarget, move: false);
            }

            if (FileTransferService.ShouldProcess(config.ConflictPolicy, source, fullTarget, conflictPrompt))
            {
                FileTransferService.CopyOrMove(source, fullTarget, move: false);
            }

            count++;
        }

        progress?.Report($"Backup complete: {count} files.");
        return count;
    }

    public Task<int> ReturnFromBackupAsync(LibraryConfig config, bool videosOnly, bool webpOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var videoExtensions = config.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(config.BackupRoot, "*.*", SearchOption.AllDirectories))
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

            if (!videosOnly && !webpOnly && !config.TrackExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(config.BackupRoot, file);
            var target = ResolveMainTargetPath(config, relative);
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, file, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(file, target, move: false);
            count++;
        }

        progress?.Report($"Returned {count} files from backup.");
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
