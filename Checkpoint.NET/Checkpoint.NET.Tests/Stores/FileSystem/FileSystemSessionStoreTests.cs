using Checkpoint.NET.Models;
using Checkpoint.NET.Settings;
using Checkpoint.NET.Stores.FileSystem;
using FluentAssertions;

namespace Checkpoint.NET.Tests.Stores.FileSystem;

public class FileSystemSessionStoreTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileSystemStoreOptions _defaultOptions;

    public FileSystemSessionStoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _defaultOptions = new FileSystemStoreOptions
        {
            EnsureDirectoryExists = true,
            ValidatePermissionsOnStartup = true,
            FallbackPath = null
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    // Helper to create a test session
    private SessionCheckpoint CreateTestSession(Guid? id = null)
    {
        return new SessionCheckpoint
        {
            SessionId = id ?? Guid.NewGuid(),
            KvCacheBytes = new byte[] { 100, 110, 130, 140 },
            TokenHistory = new int[] { 1, 2, 3, 4 },
            ModelFingerprint = "test-model-v1",
            SamplingConfig = new SamplingData { Temperature = 0.9f, TopP = 0.85f },
            LastUpdated = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "env", "test" } }
        };
    }

    // -------------------- Constructor Tests --------------------

    [Fact]
    public void Constructor_WithDefaultOptions_ShouldCreateDirectoryAndValidatePermissions()
    {
        // Act
        var store = new FileSystemSessionStore(_testRoot);

        // Assert
        var expectedPath = Path.Combine(_testRoot, "sessions");
        Directory.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithEnsureDirectoryExistsFalseAndValidatePermissionsOff_ShouldNotCreateDirectory()
    {
        // Arrange
        var options = new FileSystemStoreOptions
        {
            EnsureDirectoryExists = false,
            ValidatePermissionsOnStartup = false
        };
        var path = Path.Combine(_testRoot, "nonexistent");

        // Act
        var store = new FileSystemSessionStore(path, options);

        // Assert
        var expectedPath = Path.Combine(path, "sessions");
        Directory.Exists(expectedPath).Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithValidatePermissionsOnStartupTrue_ShouldCreateDirectoryAndValidate()
    {
        // Arrange
        var options = new FileSystemStoreOptions
        {
            ValidatePermissionsOnStartup = true
        };

        // Act
        var store = new FileSystemSessionStore(_testRoot, options);

        // Assert
        var expectedPath = Path.Combine(_testRoot, "sessions");
        Directory.Exists(expectedPath).Should().BeTrue();
    }

    // -------------------- SaveAsync Tests --------------------

    [Fact]
    public async Task SaveAsync_ShouldSaveSessionToDisk()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var session = CreateTestSession();

        // Act
        await store.SaveAsync(session);

        // Assert
        var sessionDir = Path.Combine(_testRoot, "sessions", session.SessionId.ToString());
        Directory.Exists(sessionDir).Should().BeTrue();
        File.Exists(Path.Combine(sessionDir, "kv.bin")).Should().BeTrue();
        File.Exists(Path.Combine(sessionDir, "meta.json")).Should().BeTrue();

        // Verify binary content
        var savedKv = await File.ReadAllBytesAsync(Path.Combine(sessionDir, "kv.bin"));
        savedKv.Should().BeEquivalentTo(session.KvCacheBytes);
    }

    [Fact]
    public async Task SaveAsync_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var session = CreateTestSession();

        // Act
        await store.SaveAsync(session);

        // Assert
        var sessionDir = Path.Combine(_testRoot, "sessions", session.SessionId.ToString());
        Directory.Exists(sessionDir).Should().BeTrue();
    }

    // -------------------- LoadAsync Tests --------------------

    [Fact]
    public async Task LoadAsync_ShouldLoadSessionFromDisk()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var original = CreateTestSession();
        await store.SaveAsync(original);

        // Act
        var loaded = await store.LoadAsync(original.SessionId);

        // Assert
        loaded.Should().NotBeNull();
        loaded.SessionId.Should().Be(original.SessionId);
        loaded.KvCacheBytes.Should().BeEquivalentTo(original.KvCacheBytes);
        loaded.TokenHistory.Should().BeEquivalentTo(original.TokenHistory);
        loaded.ModelFingerprint.Should().Be(original.ModelFingerprint);
        loaded.SamplingConfig.Should().BeEquivalentTo(original.SamplingConfig);
        loaded.LastUpdated.Should().BeCloseTo(original.LastUpdated, TimeSpan.FromSeconds(1));
        loaded.Tags.Should().BeEquivalentTo(original.Tags);
    }

    [Fact]
    public async Task LoadAsync_WhenSessionNotFound_ShouldReturnNull()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var nonExistentId = Guid.NewGuid();

        // Act
        var loaded = await store.LoadAsync(nonExistentId);

        // Assert
        loaded.Should().BeNull();
    }

    // -------------------- DeleteAsync Tests --------------------

    [Fact]
    public async Task DeleteAsync_ShouldDeleteSessionFolder()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var session = CreateTestSession();
        await store.SaveAsync(session);
        var sessionDir = Path.Combine(_testRoot, "sessions", session.SessionId.ToString());
        Directory.Exists(sessionDir).Should().BeTrue();

        // Act
        await store.DeleteAsync(session.SessionId);

        // Assert
        Directory.Exists(sessionDir).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WhenSessionNotFound_ShouldNotThrow()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var nonExistentId = Guid.NewGuid();

        // Act
        var act = async () => await store.DeleteAsync(nonExistentId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // -------------------- ListAsync Tests --------------------

    [Fact]
    public async Task ListAsync_ShouldReturnAllSessionIds()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var session1 = CreateTestSession();
        var session2 = CreateTestSession();
        await store.SaveAsync(session1);
        await store.SaveAsync(session2);

        // Act
        var ids = await store.ListAsync();

        // Assert
        ids.Should().Contain(session1.SessionId);
        ids.Should().Contain(session2.SessionId);
        ids.Count.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_WhenNoSessions_ShouldReturnEmpty()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);

        // Act
        var ids = await store.ListAsync();

        // Assert
        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldIgnoreTagsAndReturnAll()
    {
        // Arrange
        var store = new FileSystemSessionStore(_testRoot, _defaultOptions);
        var session = CreateTestSession();
        await store.SaveAsync(session);

        // Act
        var ids = await store.ListAsync("some", "filter");

        // Assert - FileSystem store ignores tag filtering, so it returns all.
        ids.Should().Contain(session.SessionId);
        ids.Count.Should().Be(1);
    }
}