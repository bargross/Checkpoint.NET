using Checkpoint.NET.Models;
using Checkpoint.NET.Tests.Stores.SqlServer;
using FluentAssertions;

namespace Checkpoint.NET.Tests.Stores.Mysql;

[Collection("NonParallel")]
public class SqlServerSessionStoreTests : SqlServerTestBase
{
    private SessionCheckpoint CreateTestSession(Guid? id = null)
    {
        return new SessionCheckpoint
        {
            SessionId = id ?? Guid.NewGuid(),
            KvCacheBytes = new byte[] { 100, 200, 250, 240 },
            TokenHistory = new int[] { 1, 2, 3, 4, 5 },
            ModelFingerprint = "llama-2-7b-v1",
            SamplingConfig = new SamplingData { Temperature = 0.8f, TopP = 0.95f },
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
        // Arrange
        var session = CreateTestSession();

        // Act
        await SessionStore.SaveAsync(session);

        // Assert
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
        // Arrange
        var id = Guid.NewGuid();
        var original = CreateTestSession(id);
        await SessionStore.SaveAsync(original);

        var updated = new SessionCheckpoint
        {
            SessionId = id,
            KvCacheBytes = new byte[] { 10, 20 },
            TokenHistory = new int[] { 99, 88 },
            ModelFingerprint = "updated-model",
            SamplingConfig = new SamplingData { Temperature = 0.1f },
            LastUpdated = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "updated", "yes" } }
        };

        // Act
        await SessionStore.SaveAsync(updated);

        // Assert
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
        // Arrange
        var session = CreateTestSession();
        await SessionStore.SaveAsync(session);

        // Act
        await SessionStore.DeleteAsync(session.SessionId);

        // Assert
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
        // Arrange
        var id1 = await SaveAndReturnId();
        var id2 = await SaveAndReturnId();

        // Act
        var ids = await SessionStore.ListAsync();

        // Assert
        ids.Should().Contain(id1);
        ids.Should().Contain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldFilterByTag()
    {
        // Arrange
        var id1 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "chat" } });
        var id2 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "completion" } });

        // Act
        var ids = await SessionStore.ListAsync("type", "chat");

        // Assert
        ids.Should().Contain(id1);
        ids.Should().NotContain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_WhenNoMatches_ShouldReturnEmpty()
    {
        // Arrange
        await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "chat" } });

        // Act
        var ids = await SessionStore.ListAsync("type", "nonexistent");

        // Assert
        ids.Should().BeEmpty();
    }

    private async Task<Guid> SaveAndReturnId(Dictionary<string, string>? tags = null)
    {
        var session = CreateTestSession();
        if (tags != null)
            session.Tags = tags;
        await SessionStore.SaveAsync(session);
        return session.SessionId;
    }
}