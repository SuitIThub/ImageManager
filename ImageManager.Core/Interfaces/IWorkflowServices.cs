using ImageManager.Core.Models;

namespace ImageManager.Core.Interfaces;

public interface IFileIndexService
{
    Task<int> ScanAndIndexAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<int> RunBackupAsync(LibraryConfig config, bool currentVersionOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> ReturnFromBackupAsync(LibraryConfig config, bool videosOnly, bool webpOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface IStageService
{
    Task<int> StageTrackedAsync(LibraryConfig config, bool move, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> CopySmallBackupToStageAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> ReturnFromStageAsync(LibraryConfig config, bool videosOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> ReturnFinishedFromStageAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface IArchiveService
{
    Task<int> BackupToArchiveAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> CopyArchiveToStageAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> CopyArchiveToMainAsync(LibraryConfig config, string versionId, string qualityTier, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> CopyArchiveToMainFilteredAsync(LibraryConfig config, string versionId, string qualityTier, bool videosOnly, bool webpOnly, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface ICompressionService
{
    Task<int> CompressStageToWebpAsync(LibraryConfig config, bool highQuality, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<int> CompressMainToApkBudgetAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface IAuditService
{
    Task<IReadOnlyList<DiscrepancyRecord>> CompareAsync(string sourceRoot, string targetRoot, IEnumerable<string> sourceExtensions, Func<string, string>? extensionMap = null, CancellationToken cancellationToken = default);
}

public interface ICountsService
{
    Task<IReadOnlyList<CountSummary>> GetCountsAsync(LibraryConfig config, CancellationToken cancellationToken = default);
}

public interface IPurgeService
{
    Task<int> PurgeDirectoryAsync(string targetRoot, bool includeFiles, bool includeDirectories, CancellationToken cancellationToken = default);
}
