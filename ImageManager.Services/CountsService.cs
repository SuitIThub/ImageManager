using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class CountsService : ICountsService
{
    public Task<IReadOnlyList<CountSummary>> GetCountsAsync(LibraryConfig config, CancellationToken cancellationToken = default)
    {
        var results = new List<CountSummary>();

        var mainRoots = config.MainRoots
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mainRoots.Count == 0 && !string.IsNullOrWhiteSpace(config.MainRoot))
        {
            mainRoots.Add(config.MainRoot);
        }

        var trackExt = config.TrackExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var root in mainRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var rootLabel = GetRootLabel(root);
            results.Add(CountRoot(root, $"Main ({rootLabel}) - filtered", cancellationToken, extFilter: trackExt, config.VideoExtensions));
            results.Add(CountRoot(root, $"Main ({rootLabel}) - all", cancellationToken, extFilter: null, config.VideoExtensions));
        }

        var otherLocations = new (string Name, string Path)[]
        {
            ("Stage", config.StageRoot),
            ("Backup", config.BackupRoot),
            ("Full Backup", config.FullBackupRoot),
            ("Archive", config.ArchiveRoot)
        };

        foreach (var (name, root) in otherLocations)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            results.Add(CountRoot(root, name, cancellationToken, extFilter: null, config.VideoExtensions));
        }

        return Task.FromResult<IReadOnlyList<CountSummary>>(results);
    }

    private static CountSummary CountRoot(string root, string locationName, CancellationToken cancellationToken, ISet<string>? extFilter, IReadOnlyCollection<string> videoExtConfig)
    {
        var summary = new CountSummary { LocationName = locationName };
        var videoExtensions = videoExtConfig.Count > 0
            ? videoExtConfig.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".webm", ".mp4" };
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (extFilter is not null && !extFilter.Contains(ext))
            {
                continue;
            }

            var info = new FileInfo(file);
            summary.TotalBytes += info.Length;
            if (videoExtensions.Contains(ext))
            {
                summary.VideoCount++;
            }
            else
            {
                summary.ImageCount++;
            }
        }

        return summary;
    }

    private static string GetRootLabel(string root)
    {
        try
        {
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? root : name;
        }
        catch
        {
            return root;
        }
    }
}
