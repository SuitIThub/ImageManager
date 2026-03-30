using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using ImageManager.App.Services;
using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IConfigStore _configStore;
    private readonly IFileIndexService _fileIndexService;
    private readonly ITrackingStore _trackingStore;
    private readonly IBackupService _backupService;
    private readonly IStageService _stageService;
    private readonly IArchiveService _archiveService;
    private readonly ICompressionService _compressionService;
    private readonly IAuditService _auditService;
    private readonly ICountsService _countsService;
    private readonly IPurgeService _purgeService;
    private readonly FolderDialogService _folderDialogs;

    private string _statusText = "Ready.";
    private LibraryConfig _config = new();
    private string _trackExtensionsText = ".png; .webp; .webm";
    private string _archiveExtensionsText = ".png; .webp; .webm";
    private string _videoExtensionsText = ".webm; .mp4";
    private string _finishedExtensionsText = ".webp; .webm";
    private string _compressionInputExtensionsText = ".png; .jpg; .jpeg; .bmp; .tif; .tiff; .webp";
    private string _discrepancyMapSourceExtensionText = ".png";
    private string _discrepancyMapTargetExtensionText = ".webp";
    private string _mainRootsText = string.Empty;
    private string _conflictOverrideText = ".webm=Skip";
    private string _conversionOverridesText = "events=lossless";
    private string _compressionDetails = "No compression run yet.";
    private readonly List<string> _compressionLogLines = [];

    public MainWindowViewModel(
        IConfigStore configStore,
        ITrackingStore trackingStore,
        IFileIndexService fileIndexService,
        IBackupService backupService,
        IStageService stageService,
        IArchiveService archiveService,
        ICompressionService compressionService,
        IAuditService auditService,
        ICountsService countsService,
        IPurgeService purgeService,
        FolderDialogService folderDialogs)
    {
        _configStore = configStore;
        _trackingStore = trackingStore;
        _fileIndexService = fileIndexService;
        _backupService = backupService;
        _stageService = stageService;
        _archiveService = archiveService;
        _compressionService = compressionService;
        _auditService = auditService;
        _countsService = countsService;
        _purgeService = purgeService;
        _folderDialogs = folderDialogs;

        RunScanCommand = new RelayCommand(async () => await RunScanAsync());
        RunBackupCommand = new RelayCommand(async () => await RunBackupAsync(currentVersionOnly: false));
        RunCurrentVersionBackupCommand = new RelayCommand(async () => await RunBackupAsync(currentVersionOnly: true));
        StageMoveCommand = new RelayCommand(async () => await StageTrackedAsync(move: true));
        StageCopyCommand = new RelayCommand(async () => await StageTrackedAsync(move: false));
        CopyBackupToStageCommand = new RelayCommand(async () => await CopyBackupToStageAsync());
        ReturnAllFromStageCommand = new RelayCommand(async () => await ReturnFromStageAsync(videosOnly: false));
        ReturnVideosFromStageCommand = new RelayCommand(async () => await ReturnFromStageAsync(videosOnly: true));
        ReturnFinishedFromStageCommand = new RelayCommand(async () => await ReturnFinishedFromStageAsync());
        PurgeStageCommand = new RelayCommand(async () => await PurgeStageAsync());
        ArchiveBackupCommand = new RelayCommand(async () => await ArchiveBackupAsync());
        ArchiveToStageCommand = new RelayCommand(async () => await ArchiveToStageAsync());
        ArchiveToMainCommand = new RelayCommand(async () => await ArchiveToMainAsync());
        CompressStageCommand = new RelayCommand(async () => await CompressStageAsync());
        CompressMainBudgetCommand = new RelayCommand(async () => await CompressMainBudgetAsync());
        RefreshStageTreeCommand = new RelayCommand(async () => await LoadStageTreeAsync());
        RefreshBackupTreesCommand = new RelayCommand(async () => await LoadBackupTreesAsync());
        RunDiscrepancyCheckCommand = new RelayCommand(async () => await RunDiscrepancyCheckAsync());
        RunDiscrepancyFullBackupCommand = new RelayCommand(async () => await RunDiscrepancyFullBackupAsync());
        RunDiscrepancyArchiveCommand = new RelayCommand(async () => await RunDiscrepancyArchiveAsync());
        RunDiscrepancyBackupToMainMappedCommand = new RelayCommand(async () => await RunDiscrepancyBackupToMainMappedAsync());
        LoadCountsCommand = new RelayCommand(async () => await LoadCountsAsync());
        LoadConfigCommand = new RelayCommand(async () => await LoadConfigAsync());
        SaveConfigCommand = new RelayCommand(async () => await SaveConfigAsync());
        ApplyExtensionsCommand = new RelayCommand(ApplyExtensions);
        ApplyRulesCommand = new RelayCommand(ApplyRules);
        ApplyTreeSelectionCommand = new RelayCommand(async () => await ApplyTreeSelectionAsync());

        BrowseMainRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.MainRoot = path, Config.MainRoot, "Select Main Root folder"));
        BrowseAddMainRootCommand = new RelayCommand(_ => BrowseAddMainRoot());
        BrowseStageRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.StageRoot = path, Config.StageRoot, "Select Staging folder"));
        BrowseBackupRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.BackupRoot = path, Config.BackupRoot, "Select Backup (small) folder"));
        BrowseFullBackupRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.FullBackupRoot = path, Config.FullBackupRoot, "Select Full Backup folder"));
        BrowseArchiveRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.ArchiveRoot = path, Config.ArchiveRoot, "Select Archive Root folder"));
        BrowsePreApkBackupRootCommand = new RelayCommand(_ => BrowseFolder(path => Config.PreApkCompressionBackupRoot = path, Config.PreApkCompressionBackupRoot, "Select Pre-APK Backup folder"));
        BrowseEncoderPathCommand = new RelayCommand(_ => BrowseFile(path => Config.WebpEncoderPath = path, Config.WebpEncoderPath, "Select WebP encoder executable"));
        ReturnVideosFromBackupCommand = new RelayCommand(async () => await ReturnFromBackupAsync(videosOnly: true, webpOnly: false));
        ReturnWebpFromBackupCommand = new RelayCommand(async () => await ReturnFromBackupAsync(videosOnly: false, webpOnly: true));
        ReturnAllFromBackupCommand = new RelayCommand(async () => await ReturnFromBackupAsync(videosOnly: false, webpOnly: false));
        ReturnVideosFromArchiveCommand = new RelayCommand(async () => await ReturnFromArchiveAsync(videosOnly: true, webpOnly: false));
        ReturnWebpFromArchiveCommand = new RelayCommand(async () => await ReturnFromArchiveAsync(videosOnly: false, webpOnly: true));
        ReturnAllFromArchiveCommand = new RelayCommand(async () => await ReturnFromArchiveAsync(videosOnly: false, webpOnly: false));

        ExpandAllFileTreeCommand = new RelayCommand(_ => FileTreeView.SetExpandedAll(true));
        CollapseAllFileTreeCommand = new RelayCommand(_ => FileTreeView.SetExpandedAll(false));
        ExpandAllBackupTreeCommand = new RelayCommand(_ => BackupTreeView.SetExpandedAll(true));
        CollapseAllBackupTreeCommand = new RelayCommand(_ => BackupTreeView.SetExpandedAll(false));
        ExpandAllFullBackupTreeCommand = new RelayCommand(_ => FullBackupTreeView.SetExpandedAll(true));
        CollapseAllFullBackupTreeCommand = new RelayCommand(_ => FullBackupTreeView.SetExpandedAll(false));
        ExpandAllStageTreeCommand = new RelayCommand(_ => StageTreeView.SetExpandedAll(true));
        CollapseAllStageTreeCommand = new RelayCommand(_ => StageTreeView.SetExpandedAll(false));
    }

    public FileTreeViewModel FileTreeView { get; } = new();
    public FileTreeViewModel StageTreeView { get; } = new();
    public FileTreeViewModel BackupTreeView { get; } = new();
    public FileTreeViewModel FullBackupTreeView { get; } = new();
    public ObservableCollection<CountSummary> Counts { get; } = [];
    public ObservableCollection<DiscrepancyRecord> Discrepancies { get; } = [];

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public LibraryConfig Config
    {
        get => _config;
        private set => SetProperty(ref _config, value);
    }

    public string CompressionDetails
    {
        get => _compressionDetails;
        set => SetProperty(ref _compressionDetails, value);
    }

    public RelayCommand RunScanCommand { get; }
    public RelayCommand RunBackupCommand { get; }
    public RelayCommand RunCurrentVersionBackupCommand { get; }
    public RelayCommand StageMoveCommand { get; }
    public RelayCommand StageCopyCommand { get; }
    public RelayCommand CopyBackupToStageCommand { get; }
    public RelayCommand ReturnAllFromStageCommand { get; }
    public RelayCommand ReturnVideosFromStageCommand { get; }
    public RelayCommand ReturnFinishedFromStageCommand { get; }
    public RelayCommand PurgeStageCommand { get; }
    public RelayCommand ArchiveBackupCommand { get; }
    public RelayCommand ArchiveToStageCommand { get; }
    public RelayCommand ArchiveToMainCommand { get; }
    public RelayCommand CompressStageCommand { get; }
    public RelayCommand CompressMainBudgetCommand { get; }
    public RelayCommand RefreshStageTreeCommand { get; }
    public RelayCommand RefreshBackupTreesCommand { get; }
    public RelayCommand RunDiscrepancyCheckCommand { get; }
    public RelayCommand RunDiscrepancyFullBackupCommand { get; }
    public RelayCommand RunDiscrepancyArchiveCommand { get; }
    public RelayCommand RunDiscrepancyBackupToMainMappedCommand { get; }
    public RelayCommand LoadCountsCommand { get; }
    public RelayCommand LoadConfigCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand BrowseMainRootCommand { get; }
    public RelayCommand BrowseAddMainRootCommand { get; }
    public RelayCommand BrowseStageRootCommand { get; }
    public RelayCommand BrowseBackupRootCommand { get; }
    public RelayCommand BrowseFullBackupRootCommand { get; }
    public RelayCommand BrowseArchiveRootCommand { get; }
    public RelayCommand BrowsePreApkBackupRootCommand { get; }
    public RelayCommand BrowseEncoderPathCommand { get; }
    public RelayCommand ApplyExtensionsCommand { get; }
    public RelayCommand ApplyRulesCommand { get; }
    public RelayCommand ApplyTreeSelectionCommand { get; }
    public RelayCommand ReturnVideosFromBackupCommand { get; }
    public RelayCommand ReturnWebpFromBackupCommand { get; }
    public RelayCommand ReturnAllFromBackupCommand { get; }
    public RelayCommand ReturnVideosFromArchiveCommand { get; }
    public RelayCommand ReturnWebpFromArchiveCommand { get; }
    public RelayCommand ReturnAllFromArchiveCommand { get; }
    public RelayCommand ExpandAllFileTreeCommand { get; }
    public RelayCommand CollapseAllFileTreeCommand { get; }
    public RelayCommand ExpandAllBackupTreeCommand { get; }
    public RelayCommand CollapseAllBackupTreeCommand { get; }
    public RelayCommand ExpandAllFullBackupTreeCommand { get; }
    public RelayCommand CollapseAllFullBackupTreeCommand { get; }
    public RelayCommand ExpandAllStageTreeCommand { get; }
    public RelayCommand CollapseAllStageTreeCommand { get; }
    public Array ConflictActions => Enum.GetValues<ConflictAction>();

    public string TrackExtensionsText
    {
        get => _trackExtensionsText;
        set => SetProperty(ref _trackExtensionsText, value);
    }

    public string ArchiveExtensionsText
    {
        get => _archiveExtensionsText;
        set => SetProperty(ref _archiveExtensionsText, value);
    }

    public string VideoExtensionsText
    {
        get => _videoExtensionsText;
        set => SetProperty(ref _videoExtensionsText, value);
    }

    public string FinishedExtensionsText
    {
        get => _finishedExtensionsText;
        set => SetProperty(ref _finishedExtensionsText, value);
    }

    public string CompressionInputExtensionsText
    {
        get => _compressionInputExtensionsText;
        set => SetProperty(ref _compressionInputExtensionsText, value);
    }

    public string DiscrepancyMapSourceExtensionText
    {
        get => _discrepancyMapSourceExtensionText;
        set => SetProperty(ref _discrepancyMapSourceExtensionText, value);
    }

    public string DiscrepancyMapTargetExtensionText
    {
        get => _discrepancyMapTargetExtensionText;
        set => SetProperty(ref _discrepancyMapTargetExtensionText, value);
    }

    public string MainRootsText
    {
        get => _mainRootsText;
        set => SetProperty(ref _mainRootsText, value);
    }

    public string ConflictOverrideText
    {
        get => _conflictOverrideText;
        set => SetProperty(ref _conflictOverrideText, value);
    }

    public string ConversionOverridesText
    {
        get => _conversionOverridesText;
        set => SetProperty(ref _conversionOverridesText, value);
    }

    public async Task InitializeAsync()
    {
        await LoadConfigAsync();
        await _trackingStore.InitializeAsync();
        await LoadTreeFromTrackingAsync();
        await LoadStageTreeAsync();
        await LoadBackupTreesAsync();
    }

    private async Task LoadConfigAsync()
    {
        Config = await _configStore.LoadAsync();
        TrackExtensionsText = string.Join("; ", Config.TrackExtensions);
        ArchiveExtensionsText = string.Join("; ", Config.ArchiveExtensions);
        VideoExtensionsText = string.Join("; ", Config.VideoExtensions);
        FinishedExtensionsText = string.Join("; ", Config.FinishedExtensions);
        CompressionInputExtensionsText = string.Join("; ", Config.CompressionInputExtensions);
        DiscrepancyMapSourceExtensionText = Config.DiscrepancyMapSourceExtension;
        DiscrepancyMapTargetExtensionText = Config.DiscrepancyMapTargetExtension;
        if (Config.MainRoots.Count == 0 && !string.IsNullOrWhiteSpace(Config.MainRoot))
        {
            Config.MainRoots = [Config.MainRoot];
        }
        MainRootsText = string.Join("; ", Config.MainRoots);
        ConflictOverrideText = string.Join("; ", Config.ConflictPolicy.ExtensionOverrides.Select(kv => $"{kv.Key}={kv.Value}"));
        ConversionOverridesText = string.Join("; ", Config.ConversionOverrides.Select(r => $"{r.FolderContains}={(r.UseLossless ? "lossless" : r.Quality.ToString(CultureInfo.InvariantCulture))}"));
        StatusText = "Configuration loaded.";
    }

    private async Task SaveConfigAsync()
    {
        await _configStore.SaveAsync(Config);
        StatusText = "Configuration saved.";
    }

    private async Task RunScanAsync()
    {
        var count = await _fileIndexService.ScanAndIndexAsync(Config, new Progress<string>(s => StatusText = s));
        StatusText = $"Scan complete: {count} files indexed.";
        await LoadTreeFromTrackingAsync();
    }

    private async Task RunBackupAsync(bool currentVersionOnly)
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _backupService.RunBackupAsync(Config, currentVersionOnly, new Progress<string>(s => StatusText = s));
            StatusText = currentVersionOnly
                ? $"Current-version backup complete: {count} files copied."
                : $"Backup complete: {count} files copied.";
        });
        await LoadBackupTreesAsync();
    }

    private async Task StageTrackedAsync(bool move)
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _stageService.StageTrackedAsync(Config, move, new Progress<string>(s => StatusText = s));
            StatusText = move ? $"Staged by move: {count} files." : $"Staged by copy: {count} files.";
        });
        await LoadStageTreeAsync();
    }

    private async Task CopyBackupToStageAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _stageService.CopySmallBackupToStageAsync(Config, new Progress<string>(s => StatusText = s));
            StatusText = $"Copied backup to stage: {count} files.";
        });
        await LoadStageTreeAsync();
    }

    private async Task ReturnFromStageAsync(bool videosOnly)
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _stageService.ReturnFromStageAsync(Config, videosOnly, new Progress<string>(s => StatusText = s));
            StatusText = videosOnly ? $"Returned videos from stage: {count} files." : $"Returned all from stage: {count} files.";
        });
    }

    private async Task ReturnFinishedFromStageAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _stageService.ReturnFinishedFromStageAsync(Config, new Progress<string>(s => StatusText = s));
            StatusText = $"Returned finished files from stage: {count} files.";
        });
    }

    private async Task PurgeStageAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _purgeService.PurgeDirectoryAsync(Config.StageRoot, includeFiles: true, includeDirectories: true);
            StatusText = $"Staging purged: {count} entries removed.";
        });
        await LoadStageTreeAsync();
    }

    private async Task ArchiveBackupAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _archiveService.BackupToArchiveAsync(Config, Config.CurrentVersionId, Config.CurrentQualityTier, new Progress<string>(s => StatusText = s));
            StatusText = $"Archived {count} files to {Config.CurrentVersionId}/{Config.CurrentQualityTier}.";
        });
    }

    private async Task ArchiveToStageAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _archiveService.CopyArchiveToStageAsync(Config, Config.CurrentVersionId, Config.CurrentQualityTier, new Progress<string>(s => StatusText = s));
            StatusText = $"Copied {count} archive files to stage.";
        });
    }

    private async Task ArchiveToMainAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _archiveService.CopyArchiveToMainAsync(Config, Config.CurrentVersionId, Config.CurrentQualityTier, new Progress<string>(s => StatusText = s));
            StatusText = $"Copied {count} archive files to main.";
        });
    }

    private async Task CompressStageAsync()
    {
        StatusText = "Stage → WebP compression running…";
        AppendCompressionDetail("=== Stage → WebP started ===");
        await LoadStageTreeAsync();
        var progress = new Progress<string>(s =>
        {
            StatusText = s;
            AppendCompressionDetail(s);
        });
        await ExecuteSafelyAsync(async () =>
        {
            // Stage compression should respect DefaultLossyQuality as the default.
            // Use ConversionOverrides for lossless per-folder rules (e.g. items=lossless).
            var count = await _compressionService.CompressStageToWebpAsync(Config, highQuality: false, progress);
            StatusText = $"Prepared stage WebP outputs: {count} files.";
            AppendCompressionDetail($"=== Stage → WebP finished: {count} files converted ===");
        });
        await LoadStageTreeAsync();
    }

    private async Task CompressMainBudgetAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _compressionService.CompressMainToApkBudgetAsync(Config, new Progress<string>(s => StatusText = s));
            StatusText = $"Backed up {count} main WebP files before APK budget compression.";
        });
    }

    private async Task ReturnFromBackupAsync(bool videosOnly, bool webpOnly)
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _backupService.ReturnFromBackupAsync(Config, videosOnly, webpOnly, new Progress<string>(s => StatusText = s));
            StatusText = videosOnly
                ? $"Returned videos from backup: {count} files."
                : webpOnly
                    ? $"Returned webp files from backup: {count} files."
                    : $"Returned files from backup: {count} files.";
        });
    }

    private async Task ReturnFromArchiveAsync(bool videosOnly, bool webpOnly)
    {
        await ExecuteSafelyAsync(async () =>
        {
            var count = await _archiveService.CopyArchiveToMainFilteredAsync(
                Config,
                Config.CurrentVersionId,
                Config.CurrentQualityTier,
                videosOnly,
                webpOnly,
                new Progress<string>(s => StatusText = s));
            StatusText = videosOnly
                ? $"Returned videos from archive: {count} files."
                : webpOnly
                    ? $"Returned webp files from archive: {count} files."
                    : $"Returned files from archive: {count} files.";
        });
    }

    private async Task RunDiscrepancyCheckAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            Discrepancies.Clear();
            foreach (var root in GetConfiguredMainRoots())
            {
                var rootName = GetRootName(root);
                var rows = await _auditService.CompareAsync(
                    root,
                    Config.BackupRoot,
                    Config.TrackExtensions,
                    relative => Path.Combine(rootName, relative));
                foreach (var row in rows)
                {
                    Discrepancies.Add(row);
                }
            }

            StatusText = $"Discrepancy check complete: {Discrepancies.Count} missing counterparts.";
        });
    }

    private async Task RunDiscrepancyFullBackupAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            Discrepancies.Clear();
            foreach (var root in GetConfiguredMainRoots())
            {
                var rootName = GetRootName(root);
                var rows = await _auditService.CompareAsync(
                    root,
                    Config.FullBackupRoot,
                    Config.TrackExtensions,
                    relative => Path.Combine(rootName, relative));
                foreach (var row in rows)
                {
                    Discrepancies.Add(row);
                }
            }

            StatusText = $"Main vs full-backup discrepancy check: {Discrepancies.Count} missing.";
        });
    }

    private async Task RunDiscrepancyArchiveAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            Discrepancies.Clear();
            var archiveRoot = Path.Combine(Config.ArchiveRoot, Config.CurrentVersionId, Config.CurrentQualityTier);
            foreach (var root in GetConfiguredMainRoots())
            {
                var rootName = GetRootName(root);
                var rows = await _auditService.CompareAsync(
                    root,
                    archiveRoot,
                    Config.TrackExtensions,
                    relative => Path.Combine(rootName, relative));
                foreach (var row in rows)
                {
                    Discrepancies.Add(row);
                }
            }

            StatusText = $"Main vs archive discrepancy check: {Discrepancies.Count} missing.";
        });
    }

    private async Task RunDiscrepancyBackupToMainMappedAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            Discrepancies.Clear();
            var rows = await _auditService.CompareAsync(
                Config.BackupRoot,
                Config.MainRoot,
                [Config.DiscrepancyMapSourceExtension],
                relativePath =>
                {
                    var swapped = relativePath.Replace('\\', '/');
                    var idx = swapped.IndexOf('/');
                    if (idx >= 0)
                    {
                        swapped = swapped[(idx + 1)..];
                    }
                    if (swapped.EndsWith(Config.DiscrepancyMapSourceExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        swapped = swapped[..^Config.DiscrepancyMapSourceExtension.Length] + Config.DiscrepancyMapTargetExtension;
                    }

                    return swapped.Replace('/', Path.DirectorySeparatorChar);
                });
            foreach (var row in rows)
            {
                Discrepancies.Add(row);
            }

            StatusText = $"Backup->Main mapped discrepancy check: {Discrepancies.Count} missing.";
        });
    }

    private async Task LoadCountsAsync()
    {
        Counts.Clear();
        var data = await _countsService.GetCountsAsync(Config);
        foreach (var item in data)
        {
            Counts.Add(item);
        }

        StatusText = $"Loaded counts for {Counts.Count} locations.";
    }

    private async Task LoadTreeFromTrackingAsync()
    {
        FileTreeView.Clear();
        var records = await _trackingStore.ReadAllAsync();
        var root = new Dictionary<string, FileTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var rootName = GetRootName(string.IsNullOrWhiteSpace(record.SourceRoot) ? Config.MainRoot : record.SourceRoot);
            var displayPath = Path.Combine(rootName, record.RelativePath);
            var parts = displayPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var key = string.Empty;
            ObservableCollection<FileTreeNodeViewModel> targetCollection = FileTreeView.Roots;
            FileTreeNodeViewModel? parentNode = null;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLast = i == parts.Length - 1;
                key = string.IsNullOrEmpty(key) ? part : $"{key}/{part}";
                if (!root.TryGetValue(key, out var node))
                {
                    node = new FileTreeNodeViewModel(part, key, isFolder: !isLast)
                    {
                        Parent = parentNode
                    };
                    node.SetCheckedWithoutNotify(record.IsSelected);
                    root[key] = node;
                    targetCollection.Add(node);
                }

                parentNode = node;
                targetCollection = node.Children;
            }
        }

        foreach (var r in FileTreeView.Roots)
        {
            r.RefreshAggregateFromChildrenAfterLoad();
        }

        StatusText = $"Tree loaded with {records.Count} tracked entries.";
    }

    private async Task LoadStageTreeAsync()
    {
        var files = await LoadFolderTreeAsync(Config.StageRoot, StageTreeView, "stage");
        if (files >= 0)
        {
            AppendCompressionDetail($"Stage tree loaded: {files} files from '{Config.StageRoot}'.");
        }
    }

    private async Task LoadBackupTreesAsync()
    {
        await LoadFolderTreeAsync(Config.BackupRoot, BackupTreeView, "backup");
        await LoadFolderTreeAsync(Config.FullBackupRoot, FullBackupTreeView, "full-backup");
    }

    private static async Task<int> LoadFolderTreeAsync(string rootPath, FileTreeViewModel treeView, string fallbackName)
    {
        treeView.Clear();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return -1;
        }

        var files = await Task.Run(() => Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories).ToList());
        var rootMap = new Dictionary<string, FileTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = fallbackName;
        }

        var rootNode = new FileTreeNodeViewModel(rootName, rootName, isFolder: true);
        rootNode.SetCheckedWithoutNotify(true);
        treeView.Roots.Add(rootNode);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(rootPath, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var key = rootName;
            var folderParent = rootNode;
            var target = rootNode.Children;

            for (var i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                key = $"{key}/{parts[i]}";
                if (!rootMap.TryGetValue(key, out var node))
                {
                    node = new FileTreeNodeViewModel(parts[i], key, isFolder: !isLast)
                    {
                        Parent = folderParent
                    };
                    node.SetCheckedWithoutNotify(true);
                    rootMap[key] = node;
                    target.Add(node);
                }

                folderParent = node;
                target = node.Children;
            }
        }

        foreach (var r in treeView.Roots)
        {
            r.RefreshAggregateFromChildrenAfterLoad();
        }

        return files.Count;
    }

    private void AppendCompressionDetail(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (var line in message.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }
            _compressionLogLines.Add($"[{DateTime.Now:HH:mm:ss}] {trimmed}");
        }

        const int maxLines = 220;
        if (_compressionLogLines.Count > maxLines)
        {
            _compressionLogLines.RemoveRange(0, _compressionLogLines.Count - maxLines);
        }

        CompressionDetails = string.Join(Environment.NewLine, _compressionLogLines);
    }

    private async Task ExecuteSafelyAsync(Func<Task> action)
    {
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void BrowseFolder(Action<string> assign, string? currentValue, string title)
    {
        try
        {
            var selected = _folderDialogs.PickFolder(currentValue, title);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                assign(selected);
                RaisePropertyChanged(nameof(Config));
                StatusText = $"Selected: {selected}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void BrowseFile(Action<string> assign, string? currentValue, string title)
    {
        try
        {
            var selected = _folderDialogs.PickFile(currentValue, title);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                assign(selected);
                RaisePropertyChanged(nameof(Config));
                StatusText = $"Selected file: {selected}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void ApplyExtensions()
    {
        try
        {
            Config.MainRoots = ParsePaths(MainRootsText);
            if (Config.MainRoots.Count > 0)
            {
                Config.MainRoot = Config.MainRoots[0];
            }
            Config.TrackExtensions = ParseExtensions(TrackExtensionsText);
            Config.ArchiveExtensions = ParseExtensions(ArchiveExtensionsText);
            Config.VideoExtensions = ParseExtensions(VideoExtensionsText);
            Config.FinishedExtensions = ParseExtensions(FinishedExtensionsText);
            Config.CompressionInputExtensions = ParseExtensions(CompressionInputExtensionsText);
            Config.DiscrepancyMapSourceExtension = NormalizeExtension(DiscrepancyMapSourceExtensionText, ".png");
            Config.DiscrepancyMapTargetExtension = NormalizeExtension(DiscrepancyMapTargetExtensionText, ".webp");
            RaisePropertyChanged(nameof(Config));
            StatusText = $"Applied extensions. Track: {Config.TrackExtensions.Count}, Archive: {Config.ArchiveExtensions.Count}, Video: {Config.VideoExtensions.Count}, Finished: {Config.FinishedExtensions.Count}, Compression inputs: {Config.CompressionInputExtensions.Count}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void ApplyRules()
    {
        try
        {
            Config.ConflictPolicy.ExtensionOverrides = ParseConflictOverrides(ConflictOverrideText);
            Config.ConversionOverrides = ParseConversionOverrides(ConversionOverridesText);
            RaisePropertyChanged(nameof(Config));
            StatusText = $"Applied rules. Conflict overrides: {Config.ConflictPolicy.ExtensionOverrides.Count}, conversion overrides: {Config.ConversionOverrides.Count}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task ApplyTreeSelectionAsync()
    {
        await ExecuteSafelyAsync(async () =>
        {
            var selectedDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSelectedLeafPaths(FileTreeView.Roots, selectedDisplay);
            var all = await _trackingStore.ReadAllAsync();
            var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in all)
            {
                var sourceRoot = string.IsNullOrWhiteSpace(record.SourceRoot) ? Config.MainRoot : record.SourceRoot;
                var rootName = GetRootName(sourceRoot);
                var displayPath = Path.Combine(rootName, record.RelativePath).Replace('/', Path.DirectorySeparatorChar).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (selectedDisplay.Contains(displayPath))
                {
                    selectedKeys.Add($"{sourceRoot}|{record.RelativePath}");
                }
            }

            await _trackingStore.SetSelectionAsync(selectedKeys);
            StatusText = $"Applied tree selection to tracking index ({selectedKeys.Count} selected files).";
        });
    }

    private static void AddSelectedLeafPaths(IEnumerable<FileTreeNodeViewModel> nodes, ISet<string> selected)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder)
            {
                if (node.IsChecked == true)
                {
                    selected.Add(node.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                }

                continue;
            }

            AddSelectedLeafPaths(node.Children, selected);
        }
    }

    private static List<string> ParseExtensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parts = raw
            .Split([';', ',', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x : $".{x}")
            .Select(x => x.ToLowerInvariant())
            .Distinct()
            .ToList();

        return parts;
    }

    private static string NormalizeExtension(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var value = raw.Trim();
        if (!value.StartsWith('.'))
        {
            value = $".{value}";
        }

        return value.ToLowerInvariant();
    }

    private static List<string> ParsePaths(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void BrowseAddMainRoot()
    {
        try
        {
            var selected = _folderDialogs.PickFolder(null, "Add a Main Root folder");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            var roots = ParsePaths(MainRootsText);
            if (!roots.Contains(selected, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(selected);
            }

            MainRootsText = string.Join("; ", roots);
            ApplyExtensions();
            StatusText = $"Added main root: {selected}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private static Dictionary<string, ConflictAction> ParseConflictOverrides(string? raw)
    {
        var result = new Dictionary<string, ConflictAction>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var item in raw.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var ext = parts[0].StartsWith('.') ? parts[0] : $".{parts[0]}";
            if (Enum.TryParse<ConflictAction>(parts[1], ignoreCase: true, out var action))
            {
                result[ext] = action;
            }
        }

        return result;
    }

    private static List<ConversionRuleOverride> ParseConversionOverrides(string? raw)
    {
        var result = new List<ConversionRuleOverride>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var item in raw.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var rhs = parts[1];
            if (rhs.Equals("lossless", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ConversionRuleOverride
                {
                    FolderContains = parts[0],
                    UseLossless = true,
                    Quality = -1
                });
                continue;
            }

            if (int.TryParse(rhs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality))
            {
                result.Add(new ConversionRuleOverride
                {
                    FolderContains = parts[0],
                    UseLossless = quality < 0,
                    Quality = quality
                });
            }
        }

        return result;
    }

    private IEnumerable<string> GetConfiguredMainRoots()
    {
        if (Config.MainRoots.Count > 0)
        {
            return Config.MainRoots;
        }

        if (!string.IsNullOrWhiteSpace(Config.MainRoot))
        {
            return [Config.MainRoot];
        }

        return [];
    }

    private static string GetRootName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? "main" : name;
    }
}
