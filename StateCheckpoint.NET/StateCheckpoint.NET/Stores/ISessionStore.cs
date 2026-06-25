using StateCheckpoint.NET.Models;

namespace StateCheckpoint.NET.Stores
{
    public interface ISessionStore
    {
        Task SaveAsync(SessionCheckpoint session, CancellationToken ct = default);
        Task<SessionCheckpoint?> LoadAsync(Guid sessionId, CancellationToken ct = default);
        Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
        Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken ct = default);
    }
}
