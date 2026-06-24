using Checkpoint.NET.Models;
using FluentAssertions;

namespace Checkpoint.NET.Tests.Stores.Postgres;

[Collection("NonParallel")]
public class PostgresSessionStoreTests : PostgresTestBase
{
    private SessionCheckpoint CreateTestSession(Guid? id = null)
    {
        return new SessionCheckpoint
        {
            SessionId = id ?? Guid.NewGuid(),
            KvCacheBytes = new byte[] { 100, 200, 130, 240, 250 },
            TokenHistory = new int[] { 1, 2, 3, 4, 5 },
            ModelFingerprint = "llama-2-7b-v1",
            SamplingConfig = new SamplingData { Temperature = 0.8f, TopP = 0.95f, TopK = 50 },
            LastUpdated = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "env", "test" }, { "type", "chat" } }
        };
    }

    [Fact]
    public async Task EnsureSchemaAsync_ShouldBeIdempotent()
    {
        var act = async () => await SessionStore.EnsureSchemaAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_ShouldInsertSession()
    {
        var session = CreateTestSession();
        await SessionStore.SaveAsync(session);

        var loaded = await SessionStore.LoadAsync(session.SessionId);
        loaded.Should().NotBeNull();
        loaded.SessionId.Should().Be(session.SessionId);
        loaded.KvCacheBytes.Should().BeEquivalentTo(session.KvCacheBytes);
        loaded.TokenHistory.Should().BeEquivalentTo(session.TokenHistory);
        loaded.ModelFingerprint.Should().Be(session.ModelFingerprint);
        loaded.SamplingConfig.Should().BeEquivalentTo(session.SamplingConfig);
        loaded.LastUpdated.Should().BeCloseTo(session.LastUpdated, TimeSpan.FromSeconds(1));
        loaded.Tags.Should().BeEquivalentTo(session.Tags);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExistingSession()
    {
        var id = Guid.NewGuid();
        var original = CreateTestSession(id);
        await SessionStore.SaveAsync(original);

        var updated = new SessionCheckpoint
        {
            SessionId = id,
            KvCacheBytes = new byte[] { 10, 20, 30 },
            TokenHistory = new int[] { 99, 88, 77 },
            ModelFingerprint = "updated-model",
            SamplingConfig = new SamplingData { Temperature = 0.1f, TopP = 0.5f },
            LastUpdated = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "updated", "yes" } }
        };
        await SessionStore.SaveAsync(updated);

        var loaded = await SessionStore.LoadAsync(id);
        loaded.Should().NotBeNull();
        loaded.KvCacheBytes.Should().BeEquivalentTo(updated.KvCacheBytes);
        loaded.TokenHistory.Should().BeEquivalentTo(updated.TokenHistory);
        loaded.ModelFingerprint.Should().Be(updated.ModelFingerprint);
        loaded.SamplingConfig.Should().BeEquivalentTo(updated.SamplingConfig);
        loaded.Tags.Should().BeEquivalentTo(updated.Tags);
    }

    [Fact]
    public async Task LoadAsync_WhenNotFound_ShouldReturnNull()
    {
        var loaded = await SessionStore.LoadAsync(Guid.NewGuid());
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteSession()
    {
        var session = CreateTestSession();
        await SessionStore.SaveAsync(session);
        await SessionStore.DeleteAsync(session.SessionId);

        var loaded = await SessionStore.LoadAsync(session.SessionId);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenNotExists_ShouldNotThrow()
    {
        var act = async () => await SessionStore.DeleteAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnAllSessionIds()
    {
        var id1 = await SaveAndReturnId();
        var id2 = await SaveAndReturnId();
        var ids = await SessionStore.ListAsync();
        ids.Should().Contain(id1);
        ids.Should().Contain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldFilterByTag()
    {
        var id1 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "chat" } });
        var id2 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "completion" } });
        var ids = await SessionStore.ListAsync("type", "chat");
        ids.Should().Contain(id1);
        ids.Should().NotContain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_WhenNoMatches_ShouldReturnEmpty()
    {
        await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "chat" } });
        var ids = await SessionStore.ListAsync("type", "nonexistent");
        ids.Should().BeEmpty();
    }

    private async Task<Guid> SaveAndReturnId(Dictionary<string, string>? tags = null)
    {
        var session = CreateTestSession();
        if (tags != null) session.Tags = tags;
        await SessionStore.SaveAsync(session);
        return session.SessionId;
    }
}