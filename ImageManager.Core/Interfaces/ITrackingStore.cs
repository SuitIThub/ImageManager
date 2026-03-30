using ImageManager.Core.Models;

namespace ImageManager.Core.Interfaces;

public interface ITrackingStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpsertBatchAsync(IEnumerable<FileRecord> records, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileRecord>> ReadAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileRecord>> ReadSelectedAsync(CancellationToken cancellationToken = default);
    Task SetSelectionAsync(ISet<string> selectedKeys, CancellationToken cancellationToken = default);
}
