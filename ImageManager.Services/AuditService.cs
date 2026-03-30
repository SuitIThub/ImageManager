using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Services;

public sealed class AuditService : IAuditService
{
    public Task<IReadOnlyList<DiscrepancyRecord>> CompareAsync(string sourceRoot, string targetRoot, IEnumerable<string> sourceExtensions, Func<string, string>? extensionMap = null, CancellationToken cancellationToken = default)
    {
        var allowed = sourceExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DiscrepancyRecord>();

        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(source);
            if (!allowed.Contains(ext))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, source);
            var mapped = extensionMap is null ? relative : extensionMap(relative);
            var expected = Path.Combine(targetRoot, mapped);

            if (!File.Exists(expected))
            {
                result.Add(new DiscrepancyRecord
                {
                    RelativePath = relative,
                    Issue = "Missing counterpart",
                    ExpectedLocation = expected,
                    ActualLocation = source
                });
            }
        }

        return Task.FromResult<IReadOnlyList<DiscrepancyRecord>>(result);
    }
}
