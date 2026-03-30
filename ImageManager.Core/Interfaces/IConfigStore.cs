using ImageManager.Core.Models;

namespace ImageManager.Core.Interfaces;

public interface IConfigStore
{
    Task<LibraryConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LibraryConfig config, CancellationToken cancellationToken = default);
}
