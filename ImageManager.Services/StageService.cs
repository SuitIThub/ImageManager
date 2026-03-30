using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class StageService(ITrackingStore trackingStore, IConflictPrompt? conflictPrompt = null) : IStageService
{
    public async Task<int> StageTrackedAsync(LibraryConfig config, bool move, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var records = await trackingStore.ReadSelectedAsync(cancellationToken);
        var count = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceRoot = string.IsNullOrWhiteSpace(record.SourceRoot) ? config.MainRoot : record.SourceRoot;
            var source = Path.Combine(sourceRoot, record.RelativePath);
            var target = Path.Combine(config.StageRoot, BuildTargetRelative(record, sourceRoot));
            if (!File.Exists(source) || !FileTransferService.ShouldProcess(config.ConflictPolicy, source, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(source, target, move);
            count++;
        }

        progress?.Report($"Staged {count} files.");
        return count;
    }

    public Task<int> CopySmallBackupToStageAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(config.BackupRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(config.BackupRoot, file);
            var target = Path.Combine(config.StageRoot, relative);
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, file, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(file, target, move: false);
            count++;
        }

        progress?.Report($"Copied {count} files from backup to stage.");
        return Task.FromResult(count);
    }

    public Task<int> ReturnFromStageAsync(LibraryConfig config, bool videosOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var videoExtensions = config.VideoExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(config.StageRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (videosOnly && !videoExtensions.Contains(ext))
            {
                continue;
            }

            var relative = Path.GetRelativePath(config.StageRoot, file);
            var target = ResolveMainTargetPath(config, relative);
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, file, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(file, target, move: false);
            count++;
        }

        progress?.Report($"Returned {count} files from stage.");
        return Task.FromResult(count);
    }

    public Task<int> ReturnFinishedFromStageAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var finishedExtensions = config.FinishedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (finishedExtensions.Count == 0)
        {
            progress?.Report("No FinishedExtensions configured. Nothing returned from stage.");
            return Task.FromResult(0);
        }

        foreach (var file in Directory.EnumerateFiles(config.StageRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (!finishedExtensions.Contains(ext))
            {
                continue;
            }

            var relative = Path.GetRelativePath(config.StageRoot, file);
            var target = ResolveMainTargetPath(config, relative);
            if (!FileTransferService.ShouldProcess(config.ConflictPolicy, file, target, conflictPrompt))
            {
                continue;
            }

            FileTransferService.CopyOrMove(file, target, move: false);
            count++;
        }

        progress?.Report($"Returned {count} finished files from stage.");
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
