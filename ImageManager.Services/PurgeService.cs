using ImageManager.Core.Interfaces;

namespace ImageManager.Services;

public sealed class PurgeService : IPurgeService
{
    public Task<int> PurgeDirectoryAsync(string targetRoot, bool includeFiles, bool includeDirectories, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(targetRoot))
        {
            return Task.FromResult(0);
        }

        var count = 0;
        if (includeFiles)
        {
            foreach (var file in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
                count++;
            }
        }

        if (includeDirectories)
        {
            foreach (var dir in Directory.EnumerateDirectories(targetRoot, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Delete(dir, recursive: true);
                count++;
            }
        }

        return Task.FromResult(count);
    }
}
