using StateCheckpoint.NET.Models;

namespace StateCheckpoint.NET.Stores
{
    public interface IModelStore
    {
        Task SaveAsync(ModelCheckpoint checkpoint, CancellationToken cancellationToken = default);
        Task<ModelCheckpoint?> LoadAsync(Guid modelId, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default);
        Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken cancellationToken = default);
    }
}
